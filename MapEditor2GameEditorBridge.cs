using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ETS2_Assist_GUI
{
    internal static class MapEditor2GameEditorBridge
    {
        private sealed record EditorPosition(double X, double Y, double Z);
        private static readonly object Sync = new();
        private static readonly List<WeakReference<WebView2>> WebViews = new();
        private static readonly Regex CoordinateRegex = new(
            @"\[\s*([-+]?\d+(?:\.\d+)?)\s*,\s*([-+]?\d+(?:\.\d+)?)\s*,\s*([-+]?\d+(?:\.\d+)?)\s*\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly ManualResetEventSlim StopEvent = new(false);
        private static Thread? _pollThread;
        private static bool _initialized;
        private static bool _shutdown;
        private static int _scriptSequence;
        private static bool _diagnosticCycle;
        private static bool _coordinatesAvailable;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);
        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [ModuleInitializer]
        internal static void Initialize()
        {
            lock (Sync)
            {
                if (_initialized) return;
                _initialized = true;
                _shutdown = false;
            }
            Log("Map Editor bridge: инициализация");
            Log("Map Editor bridge: запускаю фоновый поиск раз в секунду");
            Application.Idle += OnApplicationIdle;
            Application.ApplicationExit += OnApplicationExit;
            _pollThread = new Thread(PollLoop)
            {
                IsBackground = true,
                Name = "MapEditor2.GameEditorCoordinateReader"
            };
            _pollThread.Start();
        }

        private static void OnApplicationIdle(object? sender, EventArgs e)
        {
            if (Volatile.Read(ref _shutdown)) return;
            try
            {
                foreach (Form form in Application.OpenForms)
                {
                    if (!string.Equals(form.GetType().Name, "MapEditor2Form", StringComparison.Ordinal)) continue;
                    var webView = FindWebView(form);
                    if (webView == null || webView.IsDisposed) continue;
                    RegisterWebView(form, webView);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Map Editor bridge: ошибка поиска WebView2: {ex.Message}");
            }
        }

        private static void OnApplicationExit(object? sender, EventArgs e) => Shutdown();

        private static WebView2? FindWebView(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is WebView2 webView) return webView;
                var nested = FindWebView(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private static void RegisterWebView(Form form, WebView2 webView)
        {
            bool added;
            lock (Sync)
            {
                for (int i = WebViews.Count - 1; i >= 0; i--)
                {
                    if (!WebViews[i].TryGetTarget(out var existing) || existing.IsDisposed)
                        WebViews.RemoveAt(i);
                }
                added = true;
                foreach (var reference in WebViews)
                {
                    if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, webView))
                    {
                        added = false;
                        break;
                    }
                }
                if (added) WebViews.Add(new WeakReference<WebView2>(webView));
            }
            if (!added) return;

            Log("Map Editor bridge: WebView2 найден");
            EventHandler<CoreWebView2NavigationCompletedEventArgs> navigationHandler = (_, args) =>
            {
                try
                {
                    if (!args.IsSuccess)
                    {
                        Log($"Map Editor bridge: загрузка Map Editor 2 завершилась с ошибкой {args.WebErrorStatus}");
                        return;
                    }
                    Log("Map Editor bridge: страница Map Editor 2 загружена");
                    InstallPageBridge(form, webView);
                }
                catch (Exception ex)
                {
                    Log($"Map Editor bridge: ошибка NavigationCompleted: {ex.Message}");
                }
            };

            try
            {
                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.NavigationCompleted += navigationHandler;
                    Log("Map Editor bridge: NavigationCompleted подключён");
                }
                else
                {
                    EventHandler<CoreWebView2InitializationCompletedEventArgs> initializationHandler = null!;
                    initializationHandler = (_, args) =>
                    {
                        try
                        {
                            webView.CoreWebView2InitializationCompleted -= initializationHandler;
                            if (!args.IsSuccess || webView.CoreWebView2 == null)
                            {
                                Log($"Map Editor bridge: CoreWebView2 не инициализирован: {args.InitializationException?.Message}");
                                return;
                            }
                            Log("Map Editor bridge: CoreWebView2 инициализирован");
                            webView.CoreWebView2.NavigationCompleted += navigationHandler;
                            Log("Map Editor bridge: NavigationCompleted подключён");
                        }
                        catch (Exception ex)
                        {
                            Log($"Map Editor bridge: ошибка инициализации WebView2: {ex.Message}");
                        }
                    };
                    webView.CoreWebView2InitializationCompleted += initializationHandler;
                }
            }
            catch (Exception ex)
            {
                Log($"Map Editor bridge: не удалось подписаться на события WebView2: {ex.Message}");
            }
        }

        private static void InstallPageBridge(Form form, WebView2 webView)
        {
            if (Volatile.Read(ref _shutdown) || webView.IsDisposed || webView.CoreWebView2 == null) return;
            int sequence = Interlocked.Increment(ref _scriptSequence);
            try
            {
                _ = webView.CoreWebView2.ExecuteScriptAsync(BuildPageBridgeScript(sequence));
                Log("Map Editor bridge: JS-мост установлен после загрузки страницы");
            }
            catch (Exception ex)
            {
                Log($"Map Editor bridge: ошибка установки JS-моста: {ex.Message}");
            }
        }

        private static string BuildPageBridgeScript(int sequence)
        {
            return $$"""
(() => {
    try {
        const bridgeId='ets2assist-map-editor-bridge-{{sequence}}';
        if (window.__ets2AssistGameEditorBridgeId===bridgeId) return;
        window.__ets2AssistGameEditorBridgeId=bridgeId;
        const map=document.getElementById('map');
        const interaction=document.getElementById('interaction');
        const status=document.getElementById('status');
        const cursorInfo=document.getElementById('cursorInfo');
        const rightBody=document.getElementById('rightBody');
        if(!map||!interaction||!status||!cursorInfo)return;
        const markerId='ets2assist-game-editor-marker',infoId='ets2assist-game-editor-info',sepId='ets2assist-game-editor-sep',btnId='ets2assist-game-editor-center',panelId='ets2assist-game-editor-panel';

        let marker=document.getElementById(markerId);
        if(!marker){
            marker=document.createElement('div');
            marker.id=markerId;
            marker.style.cssText='position:absolute;z-index:20;display:none;width:28px;height:28px;transform:translate(-50%,-50%);pointer-events:auto;cursor:pointer;';
            marker.innerHTML='<div style="position:absolute;left:1px;right:1px;top:13px;height:2px;background:#b7ff46;box-shadow:0 0 4px #000"></div>'+
                '<div style="position:absolute;top:1px;bottom:1px;left:13px;width:2px;background:#b7ff46;box-shadow:0 0 4px #000"></div>'+
                '<div style="position:absolute;left:50%;top:8px;width:10px;height:10px;transform:translateX(-50%);border:2px solid #b7ff46;border-radius:50%;box-shadow:0 0 4px #000"></div>'+
                '<div style="position:absolute;left:50%;top:-25px;transform:translateX(-50%);white-space:nowrap;color:#b7ff46;font:400 11px Roboto,Arial,sans-serif;text-shadow:0 0 4px #000">Map editor</div>';
            marker.addEventListener('pointerdown',e=>{e.preventDefault();e.stopPropagation();},true);
            marker.addEventListener('click',e=>{
                e.preventDefault();e.stopPropagation();
                const p=window.__ets2AssistGameEditorLatest;
                if(!p||!rightBody)return;
                document.getElementById(panelId)?.remove();
                const panel=document.createElement('div');
                panel.id=panelId;
                panel.className='editPanel';
                panel.innerHTML=`<div class="editToolbar"><div class="editBtn primary" style="cursor:default;color:#b7ff46">Map editor</div></div>`+
                    `<div class="editGroup"><div class="editGroupTitle">Координаты камеры</div>`+
                    `<div class="editRow"><label class="editLabel">Координата X</label><input class="editInput" value="${p.x.toFixed(2)}" readonly></div>`+
                    `<div class="editRow"><label class="editLabel">Координата Y</label><input class="editInput" value="${p.y.toFixed(2)}" readonly></div>`+
                    `<div class="editRow"><label class="editLabel">Координата Z</label><input class="editInput" value="${p.z.toFixed(2)}" readonly></div></div>`+
                    `<div class="editMeta">Источник: окно Map editor процесса eurotrucks2.exe</div>`;
                rightBody.appendChild(panel);
            });
            map.appendChild(marker);
        }

        let info=document.getElementById(infoId);
        if(!info){info=document.createElement('span');info.id=infoId;info.style.cssText='display:none;color:#b7ff46;white-space:nowrap;font-variant-numeric:tabular-nums;';status.insertBefore(info,cursorInfo);}
        let sep=document.getElementById(sepId);
        if(!sep){sep=document.createElement('span');sep.id=sepId;sep.textContent='|';sep.style.cssText='display:none;margin:0 8px;color:#66717f;';status.insertBefore(sep,cursorInfo);}
        let center=document.getElementById(btnId);
        if(!center){
            center=document.createElement('button');center.id=btnId;center.type='button';center.textContent='⌖';center.title='Перейти к Map editor';
            center.style.cssText='display:none;width:24px;height:22px;margin:0 5px 0 4px;padding:0;border:1px solid rgba(150,165,185,.25);border-radius:3px;background:#202732;color:#b7ff46;cursor:pointer;font:400 16px Segoe UI,Arial,sans-serif;line-height:18px;';
            status.insertBefore(center,cursorInfo);
            center.addEventListener('click',e=>{e.preventDefault();e.stopPropagation();window.__ets2AssistGameEditorCenter?.();});
        }

        let lastMouseClient=null,mouseButtons=0;
        interaction.addEventListener('pointermove',e=>{if(e.isTrusted){lastMouseClient={x:e.clientX,y:e.clientY};mouseButtons=e.buttons||0;}},true);
        interaction.addEventListener('pointerdown',e=>{if(e.isTrusted)mouseButtons=e.buttons||1;},true);
        interaction.addEventListener('pointerup',e=>{if(e.isTrusted)mouseButtons=e.buttons||0;},true);
        window.addEventListener('pointerup',e=>{if(e.isTrusted)mouseButtons=0;},true);

        const parseCursor=text=>{const m=String(text||'').match(/X=\s*([-+]?\d+(?:\.\d+)?)\s+Y=\s*([-+]?\d+(?:\.\d+)?)\s+Z=\s*([-+]?\d+(?:\.\d+)?)/);return m?{x:Number(m[1]),y:Number(m[2]),z:Number(m[3])}:null;};
        const probe=(localX,localY)=>{const r=map.getBoundingClientRect();interaction.dispatchEvent(new PointerEvent('pointermove',{bubbles:true,cancelable:true,pointerId:9911,pointerType:'mouse',clientX:r.left+localX,clientY:r.top+localY,buttons:0}));return parseCursor(cursorInfo.textContent);};
        const restoreMouse=()=>{if(!lastMouseClient)return;try{interaction.dispatchEvent(new PointerEvent('pointermove',{bubbles:true,cancelable:true,pointerId:9912,pointerType:'mouse',clientX:lastMouseClient.x,clientY:lastMouseClient.y,buttons:mouseButtons}));}catch{}};
        const transform=()=>{if(mouseButtons)return null;const r=map.getBoundingClientRect(),cx=r.width/2,cy=r.height/2,p0=probe(cx,cy),px=probe(Math.min(r.width-2,cx+100),cy),pz=probe(cx,Math.min(r.height-2,cy+100));restoreMouse();if(!p0||!px||!pz)return null;const dx=px.x-p0.x,dz=pz.z-p0.z;if(!Number.isFinite(dx)||!Number.isFinite(dz)||Math.abs(dx)<1e-6||Math.abs(dz)<1e-6)return null;return{r,cx,cy,mppX:dx/100,mppZ:dz/100};};
        const render=()=>{const p=window.__ets2AssistGameEditorLatest;if(!p){info.style.display='none';sep.style.display='none';center.style.display='none';marker.style.display='none';return;}info.textContent=`Редактор: X=${p.x.toFixed(2)} Y=${p.y.toFixed(2)} Z=${p.z.toFixed(2)}`;info.style.display='inline';sep.style.display='inline';center.style.display='inline-block';const tr=transform();if(!tr)return;const p0=probe(tr.cx,tr.cy);restoreMouse();if(!p0)return;const x=tr.cx+(p.x-p0.x)/tr.mppX,y=tr.cy+(p.z-p0.z)/tr.mppZ;marker.style.left=`${x}px`;marker.style.top=`${y}px`;marker.style.display=(x>=-32&&x<=tr.r.width+32&&y>=-32&&y<=tr.r.height+32)?'block':'none';};
        window.__ets2AssistGameEditorSet=(x,y,z)=>{window.__ets2AssistGameEditorLatest={x:Number(x),y:Number(y),z:Number(z)};render();};
        window.__ets2AssistGameEditorClear=()=>{window.__ets2AssistGameEditorLatest=null;info.style.display='none';sep.style.display='none';center.style.display='none';marker.style.display='none';document.getElementById(panelId)?.remove();};
        window.__ets2AssistGameEditorCenter=()=>{const p=window.__ets2AssistGameEditorLatest;if(!p||mouseButtons)return;const tr=transform();if(!tr)return;const p0=probe(tr.cx,tr.cy);restoreMouse();if(!p0)return;const targetX=tr.cx+(p.x-p0.x)/tr.mppX,targetY=tr.cy+(p.z-p0.z)/tr.mppZ,dx=tr.cx-targetX,dy=tr.cy-targetY,cx=tr.r.left+tr.cx,cy=tr.r.top+tr.cy;interaction.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,cancelable:true,pointerId:9921,pointerType:'mouse',button:2,buttons:2,clientX:cx,clientY:cy}));interaction.dispatchEvent(new PointerEvent('pointermove',{bubbles:true,cancelable:true,pointerId:9921,pointerType:'mouse',button:2,buttons:2,clientX:cx+dx,clientY:cy+dy}));interaction.dispatchEvent(new PointerEvent('pointerup',{bubbles:true,cancelable:true,pointerId:9921,pointerType:'mouse',button:2,buttons:0,clientX:cx+dx,clientY:cy+dy}));render();};
        if(!window.__ets2AssistGameEditorTimer)window.__ets2AssistGameEditorTimer=setInterval(()=>{if(window.__ets2AssistGameEditorLatest)render();},1000);
        render();
    }catch(e){console.warn('Map Editor bridge JS error',e);}
})();
""";
        }

        private static void PollLoop()
        {
            while(!StopEvent.IsSet)
            {
                try
                {
                    var position=ReadEditorPosition();
                    UpdateAvailability(position);
                    PushPositionToWebViews(position);
                }
                catch(Exception ex)
                {
                    LogDebug($"Map Editor bridge: ошибка фонового цикла: {ex.Message}");
                    UpdateAvailability(null);
                    PushPositionToWebViews(null);
                }
                StopEvent.Wait(TimeSpan.FromSeconds(1));
            }
        }

        private static void UpdateAvailability(EditorPosition? position)
        {
            bool available=position!=null;
            bool previous=Volatile.Read(ref _coordinatesAvailable);
            Volatile.Write(ref _coordinatesAvailable,available);
            if(available)
            {
                if(!previous) Log("Есть доступ к координатам");
            }
            else if(previous)
            {
                Log("Координаты редактора не найдены");
                Volatile.Write(ref _diagnosticCycle,false);
            }
        }

        private static EditorPosition? ReadEditorPosition()
        {
            if(!Volatile.Read(ref _diagnosticCycle))
            {
                Volatile.Write(ref _diagnosticCycle,true);
                Log("Поиск координат: ищу процесс eurotrucks2.exe...");
            }
            var processIds=new HashSet<uint>();
            foreach(var process in Process.GetProcessesByName("eurotrucks2")){try{processIds.Add((uint)process.Id);}catch{}finally{process.Dispose();}}
            if(processIds.Count==0){LogDebug("Поиск координат: eurotrucks2.exe не найден");return null;}
            LogDebug($"Поиск координат: найдено процессов: {processIds.Count}");
            LogDebug("Поиск координат: ищу окно с заголовком Map editor...");
            IntPtr editorWindow=IntPtr.Zero;uint editorPid=0;string editorTitle=string.Empty;
            EnumWindows((hWnd,_)=>{GetWindowThreadProcessId(hWnd,out uint pid);if(!processIds.Contains(pid))return true;string title=GetWindowTitle(hWnd);if(title.IndexOf("Map editor",StringComparison.OrdinalIgnoreCase)<0)return true;editorWindow=hWnd;editorPid=pid;editorTitle=title;return false;},IntPtr.Zero);
            if(editorWindow==IntPtr.Zero){LogDebug("Поиск координат: окно Map editor не найдено");return null;}
            LogDebug($"Поиск координат: окно найдено PID={editorPid}, HWND=0x{editorWindow.ToInt64():X}, title=\"{editorTitle}\"");
            LogDebug("Поиск координат: создаю AutomationElement...");
            try
            {
                var root=AutomationElement.FromHandle(editorWindow);
                if(root==null){LogDebug("Поиск координат: AutomationElement.FromHandle вернул null");return null;}
                LogDebug("Поиск координат: ищу StatusBar.Pane2...");
                var pane=root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"StatusBar.Pane2",PropertyConditionFlags.IgnoreCase));
                if(pane!=null){LogDebug("Поиск координат: StatusBar.Pane2 найден");string text=GetAutomationText(pane);LogDebug($"Поиск координат: текст панели = \"{text}\"");var result=ParsePosition(text);if(result!=null)return result;}
                else LogDebug("Поиск координат: StatusBar.Pane2 не найден");
                LogDebug("Поиск координат: резервный поиск Edit-контролов с координатной строкой...");
                var edits=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit));
                foreach(AutomationElement edit in edits){string text=GetAutomationText(edit);if(!CoordinateRegex.IsMatch(text))continue;LogDebug("Поиск координат: резервный Edit-контрол найден");return ParsePosition(text);}
                LogDebug("Поиск координат: координаты не найдены");return null;
            }
            catch(Exception ex){LogDebug($"Поиск координат: UI Automation ошибка: {ex.Message}");return null;}
        }

        private static string GetAutomationText(AutomationElement element)
        {
            try{if(element.TryGetCurrentPattern(ValuePattern.Pattern,out object? pattern)&&pattern is ValuePattern value)return value.Current.Value??element.Current.Name??string.Empty;}catch{}
            try{return element.Current.Name??string.Empty;}catch{return string.Empty;}
        }

        private static EditorPosition? ParsePosition(string text)
        {
            var m=CoordinateRegex.Match(text??string.Empty);if(!m.Success)return null;
            if(!double.TryParse(m.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out double x)||!double.TryParse(m.Groups[2].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out double y)||!double.TryParse(m.Groups[3].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out double z)){LogDebug("Поиск координат: не удалось преобразовать X/Y/Z в числа");return null;}
            LogDebug($"Поиск координат: распарсено X={x.ToString("F2",CultureInfo.InvariantCulture)}, Y={y.ToString("F2",CultureInfo.InvariantCulture)}, Z={z.ToString("F2",CultureInfo.InvariantCulture)}");
            return new EditorPosition(x,y,z);
        }

        private static string GetWindowTitle(IntPtr hWnd)
        {
            try{int len=GetWindowTextLength(hWnd);if(len<=0)return string.Empty;var buffer=new char[len+1];int n=GetWindowText(hWnd,buffer,buffer.Length);return n>0?new string(buffer,0,n):string.Empty;}catch{return string.Empty;}
        }

        private static void PushPositionToWebViews(EditorPosition? position)
        {
            WeakReference<WebView2>[] views;lock(Sync)views=WebViews.ToArray();
            foreach(var reference in views){if(!reference.TryGetTarget(out var webView)||webView.IsDisposed||webView.CoreWebView2==null)continue;if(webView.FindForm() is not Form form||form.IsDisposed)continue;PushPosition(form,webView,position);}
        }

        private static void PushPosition(Form form,WebView2 webView,EditorPosition? position)
        {
            try
            {
                if(Volatile.Read(ref _shutdown)||form.IsDisposed||!form.IsHandleCreated)return;
                form.BeginInvoke(new Action(async()=>
                {
                    try
                    {
                        if(Volatile.Read(ref _shutdown)||webView.IsDisposed||webView.CoreWebView2==null)return;
                        if(position==null)await webView.CoreWebView2.ExecuteScriptAsync("window.__ets2AssistGameEditorClear?.();");
                        else{string x=position.X.ToString("R",CultureInfo.InvariantCulture),y=position.Y.ToString("R",CultureInfo.InvariantCulture),z=position.Z.ToString("R",CultureInfo.InvariantCulture);await webView.CoreWebView2.ExecuteScriptAsync($"window.__ets2AssistGameEditorSet?.({x},{y},{z});");}
                    }
                    catch(ObjectDisposedException){}
                    catch(InvalidOperationException){}
                    catch(Exception ex){LogDebug($"Map Editor bridge: ошибка передачи координат: {ex.Message}");}
                }));
            }
            catch(ObjectDisposedException){}
            catch(InvalidOperationException){}
        }

        internal static void Shutdown()
        {
            lock(Sync){if(!_initialized||_shutdown)return;_shutdown=true;}
            try{Application.Idle-=OnApplicationIdle;}catch{}
            try{Application.ApplicationExit-=OnApplicationExit;}catch{}
            try{StopEvent.Set();}catch{}
            lock(Sync)WebViews.Clear();
            Log("Map Editor bridge: остановка");
        }

        private static void Log(string message)
        {
            if(string.IsNullOrWhiteSpace(message))return;
            try{Console.WriteLine($"[MapEditor2GameEditor] {message}");}catch{}
            try{Debug.WriteLine($"[MapEditor2GameEditor] {message}");}catch{}
            try{Logger.Current?.Workflow($"[MapEditor2GameEditor] {message}");}catch{}
        }
        private static void LogDebug(string message)=>Log(message);
    }
}
