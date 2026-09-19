/* ETS2 Assist — логика интерактивного окна квестов.
 *
 * Политика показа окна полностью принадлежит приложению (категория оверлеев).
 * Здесь живёт только само окно интерактива:
 *   * сворачивание в закладку «Квесты» и обратно (закладка пульсирует, если
 *     рядом есть доступный интерактив);
 *   * эффект набора текста НПЦ и плавная смена реплик;
 *   * разделение ролевого и служебного текста.
 */
(function(){
'use strict';
var ws=null,model=null,currentQuest='',currentInteraction='',wasPaused=false,collapsed=false,questDetailPinned=false,inventoryOpen=false,archiveVisible=false,interactiveReady=false;
var pagePaused=false,hasInteractive=false,lastDialogueKey='',lastOptionsKey='',lastInteractionsKey='',lastQuestsKey='',typeTimer=null,fadeTimer=null;
/* Время последнего сворачивания/разворачивания. Один жест игрока не должен
   переключать вид дважды: двойной клик по кнопке или по прозрачной области
   давал пару «свёрнуто → развёрнуто» в одну миллисекунду, и окно визуально
   не сворачивалось. */
var lastToggleAt=0;
/* Окно открывается всегда в исходном состоянии: ранее выбранный диалог
   не восстанавливается, игрок сам выбирает интерактив слева. */
var EmptyHint='Выберите задание слева (доступные интерактивы) или активное справа.';
var $=function(id){return document.getElementById(id)};
function esc(s){return String(s==null?'':s).replace(/[&<>"']/g,function(c){return({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'})[c]})}

/* ================================================================ ДИАГНОСТИКА ВВОДА
 * ВРЕМЕННАЯ, ТОЛЬКО НАБЛЮДЕНИЕ. Ничего не меняет в поведении страницы.
 *
 * Задача: найти точное место сбоя мыши в интерактивном окне. Считаем события
 * DOM и складываем готовые строки в кольцевой буфер. Буфер забирает хост
 * (WebOverlay.exe) через ExecuteScriptAsync и пишет в файл
 * %APPDATA%\WebOverlay\quest-input-diagnostic.log.
 * Плюс те же строки печатаются в console.
 *
 * window.__questDiag.drain()    — забрать и очистить буфер событий;
 * window.__questDiag.snapshot() — счётчики и текущее состояние (без очистки);
 * window.__questDiag.state()    — paused/collapsed.
 * ================================================================ */
var qdEvents=[],qdEventsMax=400;
var qdCounters={mouseMove:0,mouseDown:0,mouseUp:0,click:0,place:0,posts:0,wsOut:0,cursorStart:0,cursorStop:0,hostMessages:0,nativeMessages:0,nativeIgnored:0,nativeClicks:0};
var qdLastMove={x:null,y:null,target:'',at:0};
var qdLastPlace={x:null,y:null,shown:false,hostX:null,hostY:null};
var qdLastState={paused:null,collapsed:null};
var qdMouseLog={lastAt:0,lastX:null,lastY:null};
var qdPlaceLogAt=0,qdMouseBound=false,qdTracking=false,qdLastQuestStateKey='';
function qdPush(line){try{qdEvents.push({t:Date.now(),l:line});if(qdEvents.length>qdEventsMax)qdEvents.splice(0,qdEvents.length-qdEventsMax)}catch(e){}}
/* Каждая строка уходит ровно в формате требования: [QUEST-DIAG]... */
function qdLog(line){
    var text='[QUEST-DIAG]'+(line.charAt(0)==='['?'':' ')+line;
    qdPush(text);
    try{console.log(text)}catch(e){}
}
function qdElementName(el){
    try{
        if(!el)return'(null)';
        if(el.nodeType===3)el=el.parentNode;
        if(!el||!el.tagName)return String(el);
        var name=el.tagName.toLowerCase();
        if(el.id)name+='#'+el.id;
        var cls=el.getAttribute&&el.getAttribute('class');
        if(cls)name+='.'+String(cls).trim().split(/\s+/).join('.');
        return name.length>90?name.slice(0,90)+'…':name;
    }catch(e){return'(?)'}
}
function qdStateText(){return'paused='+pagePaused+' collapsed='+collapsed}
/* Состояние окна логируем только при реальном изменении — иначе поток спама. */
function qdStateChanged(){
    if(qdLastState.paused===pagePaused&&qdLastState.collapsed===collapsed)return;
    qdLastState.paused=pagePaused;qdLastState.collapsed=collapsed;
    qdLog('state '+qdStateText());
}
function qdCommandText(o){
    if(!o||typeof o.command!=='string')return String(o);
    if(o.command==='set_clickable')return'set_clickable value='+(o.value===true);
    if(o.command==='set_clickable_hotspot')return'set_clickable_hotspot xr='+o.xr+' yr='+o.yr+' wr='+o.wr+' hr='+o.hr;
    return o.command;
}
/* Счётчики DOM-мыши. Отдельные слушатели: существующие обработчики НЕ трогаем. */
function qdBindMouse(){
    if(qdMouseBound)return;qdMouseBound=true;
    window.addEventListener('mousemove',function(e){
        qdCounters.mouseMove++;
        qdLastMove.x=e.clientX;qdLastMove.y=e.clientY;qdLastMove.target=qdElementName(e.target);qdLastMove.at=Date.now();
        var first=qdMouseLog.lastX===null;
        if(!first){
            if(Math.abs(e.clientX-qdMouseLog.lastX)<20&&Math.abs(e.clientY-qdMouseLog.lastY)<20)return;
            if(Date.now()-qdMouseLog.lastAt<160)return;      /* не чаще ~6/с */
        }
        qdMouseLog.lastAt=Date.now();qdMouseLog.lastX=e.clientX;qdMouseLog.lastY=e.clientY;
        qdLog('[JS-MOUSE] client='+e.clientX+','+e.clientY+' '+qdStateText()+' target='+qdLastMove.target);
    },true);
    window.addEventListener('mousedown',function(e){
        qdCounters.mouseDown++;
        qdLog('[JS-MOUSE-DOWN] button='+e.button+' client='+e.clientX+','+e.clientY+' '+qdStateText()+' target='+qdElementName(e.target));
    },true);
    window.addEventListener('mouseup',function(e){
        qdCounters.mouseUp++;
        qdLog('[JS-MOUSE-UP] button='+e.button+' client='+e.clientX+','+e.clientY);
    },true);
    window.addEventListener('click',function(e){
        qdCounters.click++;
        var extra='';
        try{
            var t=e.target;
            if(t&&t.dataset){extra=' data-index='+(t.dataset.index??'')+' data-q='+(t.dataset.q??'')+' data-i='+(t.dataset.i??'')}
        }catch(_){}
        qdLog('[CLICK] target='+qdElementName(e.target)+' x='+e.clientX+' y='+e.clientY+' '+qdStateText()+extra);
    },true);
}
window.__questDiag={
    drain:function(){var out=qdEvents.slice(0);qdEvents.length=0;return out},
    snapshot:function(){return{
        mouseMove:qdCounters.mouseMove,mouseDown:qdCounters.mouseDown,mouseUp:qdCounters.mouseUp,
        click:qdCounters.click,place:qdCounters.place,posts:qdCounters.posts,wsOut:qdCounters.wsOut,
        cursorStart:qdCounters.cursorStart,cursorStop:qdCounters.cursorStop,
        hostMessages:qdCounters.hostMessages,nativeMessages:qdCounters.nativeMessages,
        nativeIgnored:qdCounters.nativeIgnored,nativeClicks:qdCounters.nativeClicks,
        lastX:qdLastMove.x===null?-1:qdLastMove.x,lastY:qdLastMove.y===null?-1:qdLastMove.y,lastTarget:qdLastMove.target,
        paused:pagePaused,collapsed:collapsed,
        cursorDotShown:!!(cursorEl&&cursorEl.style.display==='block'),
        cursorDotX:qdLastPlace.x===null?-1:qdLastPlace.x,cursorDotY:qdLastPlace.y===null?-1:qdLastPlace.y,
        hostCursorX:qdLastPlace.hostX===undefined?-1:qdLastPlace.hostX,
        hostCursorY:qdLastPlace.hostY===undefined?-1:qdLastPlace.hostY,
        softCursorMessages:softCursorMessages,
        cursorElExists:!!(cursorEl||$('cursorDot'))
    }},
    state:function(){return{paused:pagePaused,collapsed:collapsed}}
};
qdBindMouse();
setInterval(function(){
    /* Пишем ВСЕГДА (в т.ч. при count=0): именно нулевой счётчик доказывает,
       что цепочка Windows/WebView2 → DOM не работает. */
    qdLog('[JS-MOUSE-SUMMARY] count='+qdCounters.mouseMove+' last='+qdLastMove.x+','+qdLastMove.y+' lastTarget='+qdLastMove.target+' clicks='+qdCounters.click+' mousedown='+qdCounters.mouseDown+' '+qdStateText());
},5000);

function send(o){
    var sent=false;
    if(ws&&ws.readyState===WebSocket.OPEN){ws.send(JSON.stringify(o));sent=true}
    if(o&&typeof o.command==='string'&&o.command.indexOf('quest_')===0){qdCounters.wsOut++;qdLog('[WS-OUT] command='+o.command+' sent='+sent)}
}
function post(o){
    var sent=false;
    try{if(window.chrome&&window.chrome.webview){window.chrome.webview.postMessage(JSON.stringify(o));sent=true}}catch(e){}
    qdCounters.posts++;
    qdLog('POST '+qdCommandText(o)+' sent='+sent);
}
function setNativeClickable(value){post({command:'set_clickable',value:!!value})}

/* ================================================================ INPUT SURFACE
 * v1.0.40.63: КЛИКАБЕЛЬНОСТЬ КВЕСТОВ ЧЕРЕЗ ОТДЕЛЬНОЕ ОКНО.
 *
 * ДИАГНОЗ: окно с активным LWA_COLORKEY (TransparencyKey) Windows ПОЛНОСТЬЮ
 * исключает из desktop hit-test — WindowFromPoint возвращает игру, WM_NCHITTEST
 * реально в WndProc не приходит, хотя прямой SendMessage отвечает HTCLIENT.
 * Без color-key (LWA_ALPHA) hit-test сразу попадает в WebView2 и DOM mousemove
 * оживает.
 *
 * РЕШЕНИЕ: страница ОДНА (визуальная), а мышь принимает отдельное голое
 * native-окно InteractiveQuestForm — без WebView2, без color-key. Его события
 * приходят сюда через PostWebMessageAsJson и превращаются в DOM-события.
 * Геометрия отправляется в долях viewport'а (не зависит от DPI). */
var lastInteractiveKey='';
function publishInteractiveBounds(){
    var mode='hidden',el=null;
    if(pagePaused){
        if(inventoryOpen){mode='inventory';el=$('inventoryWindow')}
        else if(collapsed){mode='quest-tab';el=$('questTab')}
        else{mode='quest-window';el=$('questWindow')}
    }
    if(!el){post({command:'set_interactive_bounds',mode:'hidden',x:0,y:0,w:0,h:0});lastInteractiveKey='hidden';return}
    var r=el.getBoundingClientRect(),vw=Math.max(1,innerWidth),vh=Math.max(1,innerHeight);
    var key=mode+'|'+Math.round(r.left)+'|'+Math.round(r.top)+'|'+Math.round(r.width)+'|'+Math.round(r.height);
    if(key===lastInteractiveKey)return;
    lastInteractiveKey=key;
    post({command:'set_interactive_bounds',mode:mode,
        x:Math.max(0,Math.min(1,r.left/vw)),y:Math.max(0,Math.min(1,r.top/vh)),
        w:Math.max(0,Math.min(1,r.width/vw)),h:Math.max(0,Math.min(1,r.height/vh))});
}
var qnPressedTarget=null,qnHoverTarget=null,qnHostBound=false,qnLastHostLogAt=0;
function qnParseHostData(data){
    if(data&&typeof data==='object')return data;
    if(typeof data==='string'){try{return JSON.parse(data)}catch(e){return null}}
    return null;
}
function qnClickableTarget(el){
    if(!el)return null;
    try{
        if(el.closest){
            var c=el.closest('button,a,input,select,textarea,summary,[role="button"],[onclick]');
            if(c)return c;
        }
    }catch(e){}
    return el;
}
function qnIsAllowedPointTarget(target){
    if(!target||!pagePaused)return false;
    if(collapsed){
        var tab=$('questTab');
        return !!tab&&(target===tab||tab.contains(target));
    }
    var app=$('questApp');
    return !!app&&(target===app||app.contains(target));
}
function qnMouseEvent(type,x,y,button,buttons,detail){
    return new MouseEvent(type,{view:window,bubbles:true,cancelable:true,
        clientX:x,clientY:y,screenX:0,screenY:0,button:button||0,buttons:buttons||0,detail:detail||0});
}
function qnWheelEvent(x,y,delta,buttons){
    try{return new WheelEvent('wheel',{view:window,bubbles:true,cancelable:true,clientX:x,clientY:y,
        deltaX:0,deltaY:-Number(delta||0),deltaZ:0,deltaMode:0,button:0,buttons:buttons||0})}
    catch(e){return new MouseEvent('wheel',{view:window,bubbles:true,cancelable:true,clientX:x,clientY:y,button:0,buttons:buttons||0})}
}
function qnSetHoverTarget(target){
    if(qnHoverTarget===target)return;
    if(qnHoverTarget&&qnHoverTarget.classList)qnHoverTarget.classList.remove('quest-native-hover');
    qnHoverTarget=target||null;
    if(qnHoverTarget&&qnHoverTarget.classList)qnHoverTarget.classList.add('quest-native-hover');
}
function dispatchNativeMouse(msg){
    var x=Number(msg.x),y=Number(msg.y);
    if(!Number.isFinite(x)||!Number.isFinite(y)){
        qdCounters.nativeIgnored++;
        return;
    }
    var type=String(msg.type||'');
    var button=Number.isFinite(Number(msg.button))?Number(msg.button):0;
    var buttons=Number.isFinite(Number(msg.buttons))?Number(msg.buttons):0;
    if(type==='mousemove')placeCursor(x,y);

    var raw=document.elementFromPoint(x,y);
    var target=qnClickableTarget(raw);
    if(!qnIsAllowedPointTarget(target)){
        qdCounters.nativeIgnored++;
        if(type==='mousemove')qnSetHoverTarget(null);
        if(type==='mouseup')qnPressedTarget=null;
        return;
    }
    if(type==='mousemove'){
        qnSetHoverTarget(target);
        target.dispatchEvent(qnMouseEvent('mousemove',x,y,0,buttons,0));
        return;
    }
    if(type==='mousedown'){
        if(target.disabled){qnPressedTarget=null;return;}
        qnPressedTarget=target;
        target.dispatchEvent(qnMouseEvent('mousedown',x,y,button,buttons,1));
        return;
    }
    if(type==='mouseup'){
        target.dispatchEvent(qnMouseEvent('mouseup',x,y,button,buttons,1));
        var pressed=qnPressedTarget;
        qnPressedTarget=null;
        if(button===0&&pressed===target&&!target.disabled){
            // dispatchEvent(mouseup) does not synthesize the browser's click event.
            // Use element.click() for native controls so their EXISTING onclick handlers run.
            if(typeof target.click==='function')target.click();
            else target.dispatchEvent(qnMouseEvent('click',x,y,0,0,1));
            qdCounters.nativeClicks++;
        }
        return;
    }
    if(type==='wheel'){
        target.dispatchEvent(qnWheelEvent(x,y,Number(msg.wheelDelta||0),buttons));
    }
}
var rawInputDiagEl=null;
var softCursorMessages=0;
function rawInputFmt(value){
    var n=Number(value);
    if(!Number.isFinite(n))return '?';
    return (n>=0?'+':'')+n;
}
function updateRawInputDiagnostics(msg){
    rawInputDiagEl=rawInputDiagEl||$('rawInputDebug');
    if(!rawInputDiagEl)return;
    rawInputDiagEl.style.display='block';
    rawInputDiagEl.innerHTML=[
        '<strong>SOFT CURSOR R12 1.0.40.82</strong>',
        'status: '+(msg.registered?'REGISTERED':'REGISTER FAILED')+' / '+(msg.softCursorActive?'ACTIVE':'INACTIVE'),
        'packets: '+(msg.packets??0),
        'last dx: '+rawInputFmt(msg.dx)+'   dy: '+rawInputFmt(msg.dy),
        'sum dx: '+rawInputFmt(msg.totalDx)+'   dy: '+rawInputFmt(msg.totalDy),
        '<strong>soft cursor: '+(Number(msg.cursorX)>=0?Number(msg.cursorX)+','+Number(msg.cursorY):'NO POSITION')+'</strong>',
        'sync corner: '+(Number(msg.syncCursorX)>=0?Number(msg.syncCursorX)+','+Number(msg.syncCursorY):'NOT SET'),
        'flags: 0x'+Number(msg.flags||0).toString(16).padStart(4,'0'),
        'buttons: 0x'+Number(msg.buttonFlags||0).toString(16).padStart(4,'0')+' data='+Number(msg.buttonData||0),
        'device: '+(msg.device||'0x0'),
        'last: '+(msg.lastRawUtc||'-')
    ].join('<br>');
}

function bindQuestNativeInput(){
    if(qnHostBound)return;
    try{
        if(!(window.chrome&&window.chrome.webview&&window.chrome.webview.addEventListener))return;
        qnHostBound=true;
        window.chrome.webview.addEventListener('message',function(ev){
            qdCounters.hostMessages++;
            var msg=qnParseHostData(ev&&ev.data);
            if(msg&&msg.source==='quest-raw-input-test'){
                updateRawInputDiagnostics(msg);
                return;
            }
            if(!msg||msg.source!=='quest-native-input')return;
            qdCounters.nativeMessages++;
            softCursorMessages++;
            /* v1.0.40.79: TAB больше не переключается из native-input host.
               Единственный источник TAB — MainForm low-level keyboard hook.
               Так исключаем двойное переключение, когда host и приложение
               одновременно видят одну физическую клавишу. */
            if(msg.type==='mousemove' && Number.isFinite(Number(msg.x)) && Number.isFinite(Number(msg.y))){
                qdLastPlace.hostX=Number(msg.x);qdLastPlace.hostY=Number(msg.y);
            }
            dispatchNativeMouse(msg);
            var now=Date.now();
            if(qdCounters.nativeMessages<=8||now-qnLastHostLogAt>=500){
                qnLastHostLogAt=now;
                qdLog('[NATIVE-IN] type='+msg.type+' x='+msg.x+' y='+msg.y+' button='+(msg.button??0)+' buttons='+(msg.buttons??0)+' target='+(qnHoverTarget?qdElementName(qnHoverTarget):'(none)')+' '+qdStateText());
            }
        });
        qdLog('[NATIVE-IN] bridge bound');
    }catch(e){
        qnHostBound=false;
        qdLog('[NATIVE-IN] bridge bind error='+e.message);
    }
}
bindQuestNativeInput();

/* ================================================================ КУРСОР
 * v1.0.40.77: СОБСТВЕННЫЙ КУРСОР СТРАНИЦЫ.
 *
 * ETS2 прячет системный курсор и рисует свой прямо в ИГРОВОЙ КАДР. Игровой кадр
 * лежит НИЖЕ окна оверлея, поэтому стрелка игры видна и двигается «под окном»:
 * ни ShowCursor, ни SetCursor, ни WM_SETCURSOR это не исправят — наш слой в
 * любом случае выше. К тому же игра уводит счётчик ShowCursor глубоко в минус.
 *
 * Поэтому стрелку рисуем САМИ, внутри страницы: #cursorDot — реальный PNG cursor.png,
 * позиционируемый по виртуальным координатам. Он часть нашей разметки, значит всегда
 * выше игрового кадра и системной стрелки.
 * R8: ETS2 не даёт нам надёжно прочитать координату своей отрисованной стрелки.
 * Поэтому при входе в паузу host принудительно ведёт физический курсор в нулевой
 * угол через относительный SendInput, а virtual cursor.png получает client=(0,0). После этого оба
 * курсора движутся по одному Raw Input dx/dy без дополнительной калибровки.
 * Прозрачность cursorDot зависит от визуального alpha текущего Quest-контента
 * и его CSS box-shadow.
 * ================================================================ */
var cursorEl=null,cursorTimer=null,cursorShown=false,cursorSystemReleased=false,cursorLastVisibleAlpha=.72;

function cssAlpha(value){
    try{
        if(!value||value==='transparent')return 0;
        var m=value.match(/rgba?\(([^)]+)\)/i);
        if(!m)return 1;
        var parts=m[1].split(',').map(function(v){return v.trim()});
        if(parts.length===4){
            var a=parseFloat(parts[3]);
            return Number.isFinite(a)?Math.max(0,Math.min(1,a)):1;
        }
        return 1;
    }catch(e){return 1}
}
function elementVisualAlpha(el){
    try{
        if(!el||el===cursorEl)return 0;
        var cs=getComputedStyle(el);
        var opacity=parseFloat(cs.opacity);
        if(!Number.isFinite(opacity)||opacity<=0)return 0;
        var bg=cssAlpha(cs.backgroundColor);
        if(bg>0)return opacity*bg;
        var border=Math.max(
            cssAlpha(cs.borderTopColor),
            cssAlpha(cs.borderRightColor),
            cssAlpha(cs.borderBottomColor),
            cssAlpha(cs.borderLeftColor)
        );
        if(border>0)return opacity*border;
        var tag=String(el.tagName||'').toLowerCase();
        if(tag==='img'||tag==='svg'||tag==='canvas'||tag==='video')return opacity;
        return 0;
    }catch(e){return 0}
}
function parseBoxShadow(value){
    try{
        if(!value||value==='none')return null;
        var colorMatch=value.match(/rgba?\([^)]*\)/i);
        var colorAlpha=colorMatch?cssAlpha(colorMatch[0]):1;
        var rest=colorMatch?value.replace(colorMatch[0],' '):value;
        var nums=rest.match(/-?\d+(?:\.\d+)?px/g)||[];
        if(nums.length<3)return null;
        return{
            dx:parseFloat(nums[0])||0,
            dy:parseFloat(nums[1])||0,
            blur:Math.max(0,parseFloat(nums[2])||0),
            spread:nums.length>=4?parseFloat(nums[3])||0:0,
            alpha:colorAlpha
        };
    }catch(e){return null}
}
function pointOutsideDistance(rect,x,y){
    var dx=Math.max(rect.left-x,0,x-rect.right);
    var dy=Math.max(rect.top-y,0,y-rect.bottom);
    return Math.sqrt(dx*dx+dy*dy);
}
function cursorSurfaceAlpha(x,y){
    if(!pagePaused)return 0;
    var root=collapsed?$('questTab'):$('questWindow');
    if(!root)return 0;
    var rr=root.getBoundingClientRect();
    if(x>=rr.left&&x<=rr.right&&y>=rr.top&&y<=rr.bottom){
        var els=[];
        try{
            if(document.elementsFromPoint)els=document.elementsFromPoint(x,y);
            else{
                var one=document.elementFromPoint(x,y);
                if(one)els=[one];
            }
        }catch(e){}
        var alpha=0;
        for(var i=0;i<els.length;i++){
            var el=els[i];
            if(!el||el===cursorEl)continue;
            alpha=Math.max(alpha,elementVisualAlpha(el));
            if(alpha>=0.995)break;
        }
        /* #questWindow/#questTab themselves are the visible opaque surface, so
           transparent child hit-test regions still retain the real surface alpha. */
        alpha=Math.max(alpha,elementVisualAlpha(root));
        return Math.max(0,Math.min(1,alpha));
    }

    /* За пределами окна оставляем курсор только там, где реально есть его тень.
       Используем текущий CSS box-shadow, чтобы и полупрозрачная тень под cursor
       не превращалась в резкое появление/исчезновение. */
    var shadow=null;
    try{shadow=parseBoxShadow(getComputedStyle(root).boxShadow)}catch(e){}
    if(!shadow||shadow.blur<=0||shadow.alpha<=0)return 0;

    var shadowRect={
        left:rr.left-shadow.spread,
        top:rr.top-shadow.spread,
        right:rr.right+shadow.spread,
        bottom:rr.bottom+shadow.spread
    };
    var d=pointOutsideDistance(shadowRect,x-shadow.dx,y-shadow.dy);
    /* v1.0.40.78: курсор не должен тускнеть сразу на первых пикселях тени.
       Даём 50 px запаса: внутри этого буфера сохраняем текущую alpha тени,
       затем запускаем прежнюю плавную quadratic-кривую затухания. */
    var fadeDistance=Math.max(0,d-50);
    if(fadeDistance>=shadow.blur)return 0;
    var t=1-fadeDistance/shadow.blur;
    return Math.max(0,Math.min(1,shadow.alpha*t*t));
}

function placeCursor(x,y){
    cursorEl=cursorEl||$('cursorDot');   // резолвим лениво: place может вызваться первым
    if(!cursorEl)return;

    cursorEl.style.transform='translate('+x+'px,'+y+'px)';
    var alpha=cursorSurfaceAlpha(x,y);
    if(alpha>0.01)cursorLastVisibleAlpha=alpha;
    /* При alpha=0 в прозрачном месте курсор оставляем на последней позиции.
       Полное скрытие выполняется только при закрытии интерактивного shell. */
    var shownAlpha=alpha>0.01?alpha:Math.max(.18,Math.min(.9,cursorLastVisibleAlpha));
    cursorEl.style.opacity=String(shownAlpha);
    if(!cursorShown){cursorShown=true;cursorEl.style.display='block'}

    /* ДИАГНОСТИКА: первые 5 вызовов, затем не чаще 2-3 раз в секунду. */
    qdCounters.place++;
    qdLastPlace.x=x;qdLastPlace.y=y;qdLastPlace.shown=!!(cursorEl&&cursorEl.style.display==='block');
    var now=Date.now();
    if(qdCounters.place<=5||now-qdPlaceLogAt>=400){
        qdPlaceLogAt=now;
        qdLog('[CURSOR-PLACE] x='+x+' y='+y+' alpha='+alpha.toFixed(3)+' shown='+qdLastPlace.shown+' placeCount='+qdCounters.place);
    }
}

function trackCursorFromEvent(e){
    if(!pagePaused||collapsed)return;
    placeCursor(e.clientX,e.clientY);
}

function startCursorTrack(){
    cursorEl=cursorEl||$('cursorDot');
    qdCounters.cursorStart++;
    if(!qdTracking){
        qdTracking=true;
        qdLog('[CURSOR] start pagePaused='+pagePaused+' collapsed='+collapsed+' cursorElementExists='+!!cursorEl+' startCount='+qdCounters.cursorStart);
    }
    if(!cursorEl)return;
    /* v1.0.40.79: the visual cursor starts at the same corner as the host-side
       physical cursor. The first Raw Input packet may arrive a little later,
       so initialize immediately instead of briefly showing the old position. */
    placeCursor(0,0);
    /* Host synchronizes the physical cursor to client=(0,0).
       The hidden WebOverlay Raw Input sink owns the physical-mouse bridge and
       sends the current client position as quest-native-input. The first packet
       is initialized from GetCursorPos when the window becomes active. */
}

function stopCursorTrack(reason){
    qdCounters.cursorStop++;
    if(qdTracking){
        qdTracking=false;
        qdLog('[CURSOR] stop reason='+(reason||'unspecified')+' cursorElementExists='+!!cursorEl+' stopCount='+qdCounters.cursorStop);
    }
    window.removeEventListener('mousemove',trackCursorFromEvent);
    window.removeEventListener('mouseover',trackCursorFromEvent);
    if(cursorTimer){clearInterval(cursorTimer);cursorTimer=null}
    qnSetHoverTarget(null);
    qnPressedTarget=null;
    if(cursorEl){cursorEl.style.display='none';cursorEl.style.opacity='0';cursorShown=false;cursorLastVisibleAlpha=.72}
}

/* Диагностика курсора из консоли страницы (аналог debugShow для этой части):
   window.__questCursor.place(300,200) — поставить стрелку принудительно. */
window.__questCursor={
    place:function(x,y){placeCursor(x,y);return !!cursorEl&&cursorEl.style.display},
    start:startCursorTrack,
    stop:stopCursorTrack,
    state:function(){return{shown:cursorShown,paused:pagePaused,collapsed:collapsed}}
};
function markerIcon(m){
    /* v1.0.40.56: иконки квестов — новые растровые Pointer_*.png (32x32).
       Соответствие: quest = «!», questdone = «?»; _on = жёлтый, _off = серый.
       Прежние SVG из editor_static_data/icons оставлены запасным вариантом
       через onerror, чтобы список не остался без иконки. */
    if(m==='yellow_exclamation')return{src:'quests/images/Pointer_quest_on_32x32.png',fb:'editor_static_data/icons/quest_exclamation_yellow.svg'};
    if(m==='yellow_question')return{src:'quests/images/Pointer_questdone_on_32x32.png',fb:'editor_static_data/icons/quest_question_yellow.svg'};
    if(m==='gray_question')return{src:'quests/images/Pointer_questdone_off_32x32.png',fb:'editor_static_data/icons/quest_question_gray.svg'};
    return null;
}

/* ---------------------------------------------------------------- сворачивание */
/* Полноэкранный оверлей квестов НЕ должен пропускать мышь иначе, чем к окну:
   в развёрнутом состоянии окно занимает центр, но прозрачные поля вокруг него
   остаются частью того же слоя. Чтобы игра не получала клики «сквозь» окно,
   контейнер перехватывает клик по прозрачной области (сворачивание). */
function applyCursorLayer(){
    /* v1.0.40.61: КУРСОР РИСУЕТ СТРАНИЦА.
       ⛔ Почему не системный/игровой курсор: ETS2 прячет систему и рисует СВОЙ
       курсор прямо в игровой кадр. Игровой кадр лежит НИЖЕ нашего окна, поэтому
       его стрелка «двигается под окном» и никакие ShowCursor/SetCursor/WM_SETCURSOR
       это не исправят — слой всегда выше. Системный курсор в игре к тому же
       уведён счётчиком ShowCursor глубоко в минус.
       РЕШЕНИЕ: скрываем курсор во всём слое (CSS `cursor:none`) и рисуем
       СОБСТВЕННУЮ стрелку (<img id="cursorDot">) по виртуальной позиции мыши.
       Изображение — реальный игровой cursor.png 27x44 для базового 1080p.
       Прозрачность стрелки вычисляется по видимому контенту/тени под ней:
       в прозрачной области стрелка исчезает, на тени становится полупрозрачной.
       Скрытие системной стрелки оставлено как дополнительная мера: если игра
       её не рисует, она не будет дублировать нашу. */
    var app=$('questApp');
    if(app)app.style.cursor=pagePaused?'none':'';
    var active=pagePaused&&!collapsed;
    /* Системная стрелка не является курсором квестов. ETS2 удерживает её
       возле центра; наличие этой стрелки поверх страницы даёт дрожание.
       Видимый курсор теперь только #cursorDot. */
    if(!cursorSystemReleased){
        post({command:'set_cursor',value:false});
        cursorSystemReleased=true;
    }
    if(active)startCursorTrack();else stopCursorTrack('applyCursorLayer:inactive');
}

/* Мышь окна управляется из двух состояний: активна ли пауза и свёрнуто ли окно.
   Развёрнутое окно кликабельно целиком; свёрнутое отдаёт мыши только область
   закладки у левой границы экрана; скрытое окно прозрачно для мыши. */
function syncInput(notify){
    qdLog('syncInput '+qdStateText());
    /* Софтовый курсор включается/выключается вместе с состоянием страницы.
       Внутри WebView2 не полагаемся на DOM mousemove: ETS2 может удерживать
       системный курсор в центре. Raw Input host является источником позиции. */
    applyCursorLayer();
    /* FIX v3: native-кликабельность больше НЕ переключается. Мышь принимает
       отдельное голое native-окно (без color-key), а странице нужно только
       сообщить ему, где лежит UI. */
    publishInteractiveBounds();
    if(notify)post({command:'return_focus'});
}
/* Старый native clickability-путь оставлен для справки, но НЕ вызывается:
   для web_quests.html он бесполезен (color-key исключает окно из hit-test). */
function syncInputLegacy(notify){
    qdLog('syncInputLegacy '+qdStateText());
    applyCursorLayer();
    if(pagePaused&&!collapsed){
        setNativeClickable(true);
        post({command:'set_clickable_hotspot',xr:0,yr:0,wr:0,hr:0});
    }else if(pagePaused&&collapsed){
        setNativeClickable(false);
        var tab=$('questTab');
        if(tab){
            /* Доли клиентской области, а не CSS-пиксели: окно-хост не объявляет
               DPI-манифест и при масштабе экрана пиксели страницы не совпадают
               с пикселями окна. */
            var r=tab.getBoundingClientRect();
            var vw=Math.max(1,window.innerWidth),vh=Math.max(1,window.innerHeight);
            var pad=6;
            /* ⛔ ПОРЯДОК КОМАНД КРИТИЧЕН (корень «на закладку нельзя нажать»).
               В хосте кликабельность ВЫВОДИТСЯ из двух полей:
                 _clickable   — разрешена ли странице принимать мышь ВООБЩЕ;
                 _hotspot     — ЕДИНСТВЕННАЯ принимающая область внутри окна.
               `set_clickable(false)` в хосте СБРАСЫВАЕТ _hotspot (Rectangle.Empty)
               и снова делает окно прозрачным для мыши. Раньше он отправлялся
               ПЕРВЫМ, поэтому следующий set_clickable_hotspot задавал область,
               но решение NeedsClickThroughStyle = (!_clickable || _hotspot.IsEmpty)
               всё равно было true ⇒ мышь шла «сквозь» закладку.
               Поэтому: СНАЧАЛА задаём горячую область, и только ПОТОМ разрешаем
               странице принимать мышь. */
            post({command:'set_clickable_hotspot',
                xr:0,
                yr:Math.max(0,(r.top-pad)/vh),
                wr:Math.min(1,(r.width+8)/vw),
                hr:Math.min(1,(r.height+pad*2)/vh)});
            setNativeClickable(true);
        }else{
            post({command:'set_clickable_hotspot',xr:0,yr:0,wr:0,hr:0});
            setNativeClickable(false);
        }
    }else{
        setNativeClickable(false);
        post({command:'set_clickable_hotspot',xr:0,yr:0,wr:0,hr:0});
    }
    if(notify)post({command:'return_focus'});
}
function applyCollapsed(value,notify,report){
    collapsed=!!value;
    qdStateChanged();
    var w=$('questWindow'),tab=$('questTab');
    if(w)w.classList.toggle('collapsed',collapsed);
    if(tab)tab.classList.toggle('visible',collapsed);
    if(collapsed)lastDialogueKey='';
    syncInput(notify);
    /* Геометрия input-окна зависит от вида окна: пересчитаем ПОСЛЕ смены
       классов/анимации — и сразу, и по завершении перехода. */
    publishInteractiveBounds();
    setTimeout(publishInteractiveBounds,360);
    /* Приложение запоминает вид окна (свёрнуто/развёрнуто). Сообщаем только о
       действиях игрока: состояние, пришедшее ОТ приложения, а также стартовое
       состояние страницы повторно отправлять нельзя — иначе окно при загрузке
       перезапишет сохранённый вид. */
    if(report)send({command:'quest_window_state',collapsed:collapsed});
}
function collapseWindow(){if(collapsed||blockToggle())return;applyCollapsed(true,true,true)}
function expandWindow(){if(!collapsed||blockToggle())return;applyCollapsed(false,true,true)}
/* Один жест — одно переключение: повторное событие того же жеста (двойной клик,
   всплытие клика от кнопки к контейнеру) не должно отменять только что
   применённое состояние. */
function blockToggle(){var now=Date.now();if(now-lastToggleAt<250)return true;lastToggleAt=now;return false}
function setTabPulse(value){hasInteractive=!!value;var tab=$('questTab');if(tab)tab.classList.toggle('pulse',hasInteractive)}

/* ------------------------------------------------------------ набор текста */
function stopTyping(){if(typeTimer){clearInterval(typeTimer);typeTimer=null}if(fadeTimer){clearTimeout(fadeTimer);fadeTimer=null}}


function applyCollapsed(value,notify,report){
    collapsed=!!value;
    qdStateChanged();
    var w=$('questWindow'),tab=$('questTab');
    if(w)w.classList.toggle('collapsed',collapsed);
    if(tab)tab.classList.toggle('visible',collapsed);
    if(collapsed)lastDialogueKey='';
    syncInput(notify);
    /* Геометрия input-окна зависит от вида окна: пересчитаем ПОСЛЕ смены
       классов/анимации — и сразу, и по завершении перехода. */
    publishInteractiveBounds();
    setTimeout(publishInteractiveBounds,360);
    /* Приложение запоминает вид окна (свёрнуто/развёрнуто). Сообщаем только о
       действиях игрока: состояние, пришедшее ОТ приложения, а также стартовое
       состояние страницы повторно отправлять нельзя — иначе окно при загрузке
       перезапишет сохранённый вид. */
    if(report)send({command:'quest_window_state',collapsed:collapsed});
}
function collapseWindow(){if(collapsed||blockToggle())return;applyCollapsed(true,true,true)}
function expandWindow(){if(!collapsed||blockToggle())return;applyCollapsed(false,true,true)}
/* Один жест — одно переключение: повторное событие того же жеста (двойной клик,
   всплытие клика от кнопки к контейнеру) не должно отменять только что
   применённое состояние. */
function blockToggle(){var now=Date.now();if(now-lastToggleAt<250)return true;lastToggleAt=now;return false}
function setTabPulse(value){hasInteractive=!!value;var tab=$('questTab');if(tab)tab.classList.toggle('pulse',hasInteractive)}

/* ------------------------------------------------------------ набор текста */
function stopTyping(){if(typeTimer){clearInterval(typeTimer);typeTimer=null}if(fadeTimer){clearTimeout(fadeTimer);fadeTimer=null}}

function typeInto(el,text,service){
    stopTyping();
    if(!el)return;
    el.innerHTML='';
    var body=document.createElement('span');
    body.className='dialogTextRole';
    var serviceNode=null;
    if(service){serviceNode=document.createElement('div');serviceNode.className='dialogTextService';serviceNode.textContent=service}
    var full=String(text||'');
    var i=0;
    var step=Math.max(1,Math.round(full.length/55));
    typeTimer=setInterval(function(){
        i=Math.min(full.length,i+step);
        body.textContent=full.slice(0,i);
        if(serviceNode&&i>=full.length)el.appendChild(serviceNode);
        if(i>=full.length){clearInterval(typeTimer);typeTimer=null}
    },16);
    el.appendChild(body);
}

/* Реплика меняется плавно: старое уходит за 150 мс, затем набор нового. */
function renderDialogue(node){
    var speaker=$('dialogSpeaker'),text=$('dialogText'),service=$('dialogService'),img=$('dialogImage'),opts=$('dialogOptions');
    if(speaker)speaker.textContent=node.speaker||'';
    var key=(node.speaker||'')+'|'+(node.text||'')+'|'+(node.serviceText||'');
    var changed=key!==lastDialogueKey;
    lastDialogueKey=key;
    if(text){
        if(changed&&text.textContent&&text.textContent.length>1){
            text.classList.add('fading');stopTyping();
            fadeTimer=setTimeout(function(){text.classList.remove('fading');typeInto(text,node.text||'')},150);
        }else if(changed){typeInto(text,node.text||'')}
    }
    if(service)service.innerHTML=node.serviceText?'<div class="serviceBlock">'+esc(node.serviceText)+'</div>':'';
    if(img){if(node.image){img.src=node.image;img.style.display='block'}else{img.removeAttribute('src');img.style.display='none'}}
    if(opts){
        var optionsKey=JSON.stringify((node.options||[]).map(function(o){return[o.id,o.text,o.serviceText,o.requirements,o.requirementsMet,o.enabled,o.reason]}));
        if(optionsKey!==lastOptionsKey){
            lastOptionsKey=optionsKey;
            opts.innerHTML=(node.options||[]).map(function(o,i){
                var req=o.requirements?'<small class="optionRequirements">'+esc(o.requirements)+'</small>':'';
                var svc=o.serviceText?'<small class="optionService">'+esc(o.serviceText)+'</small>':'';
                var reason=o.enabled===false?'<small class="optionReason">'+esc(o.reason||'Требование не выполнено')+'</small>':'';
                return'<button class="dialogOption" data-index="'+i+'" '+(o.enabled===false?'disabled':'')+'><span class="optionText">'+esc(o.text)+'</span>'+svc+req+reason+'</button>';
            }).join('');
            opts.querySelectorAll('.dialogOption').forEach(function(btn){btn.onclick=function(){
                send({command:'quest_dialog_option',questId:currentQuest,interaction:currentInteraction,index:Number(btn.dataset.index)});
            }});
        }
    }
}

/* ------------------------------------------------------------------ панели */
function renderInteractions(){
    var el=$('interactionList');if(!el||!model)return;
    var near=(model.nearby||[]).filter(function(p){return p.Marker&&p.Marker!=='none'});
    if(!near.length){el.innerHTML='<div class="muted">Нет доступных интерактивов</div>';lastInteractionsKey='';return}
    var key=JSON.stringify([currentQuest,currentInteraction].concat(near.map(function(p){return[p.QuestId,p.InteractionId,p.Name,p.Marker,Math.round(p.distance||0)]})));
    if(key===lastInteractionsKey)return;
    lastInteractionsKey=key;
    el.innerHTML=near.map(function(p){var active=p.QuestId===currentQuest&&p.InteractionId===currentInteraction;var icon=markerIcon(p.Marker);var imgHtml=icon?'<img src="'+icon.src+'" onerror="this.onerror=null;this.src=\''+icon.fb+'\';">':'';return'<button class="sideItem'+(active?' selected':'')+'" data-q="'+esc(p.QuestId)+'" data-i="'+esc(p.InteractionId)+'">'+imgHtml+'<span class="sideMain">'+esc(p.Name)+'</span><span class="sideDist">'+Math.round(p.distance||0)+' м</span></button>'}).join('');
    el.querySelectorAll('.sideItem').forEach(function(b){b.onclick=function(){selectInteraction(b.dataset.q,b.dataset.i)}})
}
function questById(id){
    return [].concat((model&&model.availableQuests)||[],(model&&model.activeQuests)||[],(model&&model.archiveQuests)||[]).find(function(q){return q.id===id})||null;
}
function firstNearbyForQuest(id){return (model&&model.nearby||[]).find(function(p){return p.QuestId===id&&p.Marker&&p.Marker!=='none'})||null}
function renderQuests(){
    var el=$('questList');if(!el||!model)return;
    var seen={},nearIds=[];
    (model.nearby||[]).forEach(function(p){if(p&&p.QuestId&&p.Marker&&p.Marker!=='none'&&!seen[p.QuestId]){seen[p.QuestId]=true;nearIds.push(p.QuestId)}});
    var all=[].concat(model.availableQuests||[],model.activeQuests||[],model.archiveQuests||[]),byId={};
    all.forEach(function(q){byId[q.id]=q});
    var nearby=nearIds.map(function(id){return byId[id]}).filter(Boolean);
    var active=(model.activeQuests||[]).filter(function(q){return !seen[q.id]});
    var archive=model.archiveQuests||[];
    var html='';
    if(nearby.length){html+='<div class="questSectionTitle">Рядом</div>'+nearby.map(function(q){return questButton(q,'nearby')}).join('');html+='<div class="questDivider"></div>'}
    html+='<div class="questSectionTitle">Активные</div>'+active.map(function(q){return questButton(q,'active')}).join('');
    html+='<button id="archiveToggle" class="archiveToggle" type="button">'+(archiveVisible?'архив ▲':'архив ▼')+'</button>';
    if(archiveVisible)html+='<div class="questDivider"></div><div class="questSectionTitle">Архив</div>'+archive.map(function(q){return questButton(q,'archive')}).join('');
    el.innerHTML=html||'<div class="muted">Нет квестов</div>';
    el.querySelectorAll('.questItem').forEach(function(btn){btn.onclick=function(e){e.stopPropagation();var near=firstNearbyForQuest(btn.dataset.q);if(near)selectInteraction(near.QuestId,near.InteractionId);else showQuestDetail(btn.dataset.q)}});
    var ab=$('archiveToggle');if(ab)ab.onclick=function(e){e.stopPropagation();archiveVisible=!archiveVisible;lastQuestsKey='';renderQuests()}
}
function questButton(q,kind){var selected=questDetailPinned&&!currentInteraction&&currentQuest===q.id;return'<button class="questItem '+kind+(selected?' selected':'')+'" data-q="'+esc(q.id)+'"><strong>'+esc(q.title)+'</strong><span>'+esc(q.status||'')+'</span>'+((q.stepDescription||q.description)?'<em>'+esc(q.stepDescription||q.description)+'</em>':'')+'</button>'}
function renderInventory(){
    var el=$('inventoryList');if(!el||!model)return;
    var items=model.inventory||[];
    el.innerHTML=items.map(function(x){return'<button class="inventoryItem" data-item="'+esc(x.id)+'"><span>'+esc(x.name||x.id)+'</span><small>×'+esc(x.amount||1)+'</small></button>'}).join('')||'<div class="muted">пусто</div>';
    el.querySelectorAll('.inventoryItem').forEach(function(btn){btn.onclick=function(e){e.stopPropagation();showError('В разработке')}})
}
function showQuestDetail(id){
    var q=questById(id);if(!q)return;
    questDetailPinned=true;currentQuest=id;currentInteraction='';
    var speaker=$('dialogSpeaker'),text=$('dialogText'),service=$('dialogService'),opts=$('dialogOptions'),img=$('dialogImage');
    if(speaker)speaker.textContent=q.title;
    if(text)text.innerHTML='<span class="dialogTextRole">'+esc(q.description||'')+'</span>';
    if(service)service.innerHTML=(q.stepDescription?'<div class="serviceBlock">'+esc(q.stepDescription)+'</div>':'')+'<div class="questRewardTitle">Награды</div>'+((q.rewards||[]).map(function(r){return'<div class="rewardLine">'+esc(r.display||r.id)+' ×'+esc(r.amount||1)+'</div>'}).join('')||'<div class="muted">—</div>');
    if(opts)opts.innerHTML='';
    if(img){img.removeAttribute('src');img.style.display='none'}
    lastDialogueKey='';lastOptionsKey='';lastQuestsKey='';
    renderQuests();renderInventory();
}
function clearDialogue(){
    stopTyping();lastDialogueKey='';lastOptionsKey='';
    var s=$('dialogSpeaker'),t=$('dialogText'),svc=$('dialogService'),o=$('dialogOptions'),i=$('dialogImage');
    if(s)s.textContent='';if(t){t.classList.remove('fading');t.textContent=EmptyHint}if(svc)svc.innerHTML='';if(o)o.innerHTML='';if(i){i.removeAttribute('src');i.style.display='none'}
}
function selectInteraction(qid,iid){
    if(!pagePaused||!interactiveReady||!model||model.paused!==true)return;
    questDetailPinned=false;currentQuest=qid;currentInteraction=iid;
    send({command:'quest_select_interaction',questId:qid,id:iid});renderQuests()
}
function setTabPulse(on){
    hasInteractive=!!on;var tab=$('questTab');if(!tab)return;tab.classList.remove('pulse');
    if(on){void tab.offsetWidth;tab.classList.add('pulse');setTimeout(function(){tab.classList.remove('pulse')},1050)}
}
function setQuestInteractiveVisible(visible,ready,pulse){
    pagePaused=!!visible;interactiveReady=!!ready;
    var app=$('questApp');if(app)app.classList.toggle('interactiveVisible',pagePaused);
    if(!pagePaused){
        inventoryOpen=false;archiveVisible=false;collapsed=true;currentQuest='';currentInteraction='';questDetailPinned=false;
        clearDialogue();setTabPulse(false);stopCursorTrack('interactive-hidden');
        var qw=$('questWindow'),iw=$('inventoryWindow'),tab=$('questTab'),itab=$('inventoryTab');
        if(qw)qw.classList.add('collapsed');if(iw)iw.classList.remove('visible');if(tab)tab.classList.remove('visible');if(itab)itab.classList.remove('visible');
    }else{
        inventoryOpen=false;collapsed=true;
        var qw=$('questWindow'),iw=$('inventoryWindow'),tab=$('questTab'),itab=$('inventoryTab');
        if(qw){qw.classList.add('collapsed');qw.classList.remove('visible')}if(iw)iw.classList.remove('visible');
        if(tab)tab.classList.add('visible');if(itab)itab.classList.add('visible');if(pulse)setTabPulse(true);
        syncInput(false);
    }
    publishInteractiveBounds();
}
function toggleInventory(){
    if(!pagePaused||!interactiveReady)return;
    inventoryOpen=!inventoryOpen;
    var qw=$('questWindow'),iw=$('inventoryWindow');
    if(qw){qw.classList.toggle('visible',!inventoryOpen);qw.classList.toggle('collapsed',inventoryOpen)}
    if(iw)iw.classList.toggle('visible',inventoryOpen);
    var tab=$('questTab'),itab=$('inventoryTab');if(tab)tab.classList.toggle('active',!inventoryOpen);if(itab)itab.classList.toggle('active',inventoryOpen);
    renderInventory();publishInteractiveBounds();
}
function applyState(data){
    if(!$('questApp'))return;model=data;
    var paused=data.paused===true,interactive=data.interactive===true;
    if(interactive!==pagePaused)setQuestInteractiveVisible(interactive,false,false);
    var stateKey=interactive+'|'+paused+'|'+(data.selectedInteraction||'')+'|'+(data.dialogue?'1':'0');
    if(stateKey!==qdLastQuestStateKey){qdLastQuestStateKey=stateKey;qdLog('WS-IN(8085) quest_state paused='+paused+' interactive='+interactive+' selected='+(data.selectedInteraction||''))}
    if(!interactive)return;
    if(questDetailPinned){currentInteraction=''}
    else if(data.selectedInteraction){currentQuest=data.selectedQuest||currentQuest;currentInteraction=data.selectedInteraction}
    else if(data.selectedQuest){currentQuest=data.selectedQuest;currentInteraction=''}
    if(data.dialogue&&!questDetailPinned)renderDialogue(data.dialogue);
    renderQuests();renderInventory();publishInteractiveBounds();
}
function connect(){try{ws=new WebSocket('ws://localhost:8085/');ws.onmessage=function(ev){try{var d=JSON.parse(ev.data);if(d.command==='quest_state')applyState(d);else if(d.command==='quest_error')showError(d.text)}catch(e){}};ws.onclose=function(){setTimeout(connect,1500)};ws.onerror=function(){try{ws.close()}catch(e){}}}catch(e){setTimeout(connect,1500)}}
function showError(text){var e=$('overlayError');if(!e)return;e.textContent=text||'Ошибка';e.classList.add('show');setTimeout(function(){e.classList.remove('show')},2500)}

/* Команды приложения, адресованные именно окну квестов. */
window.onEts2Command=function(d){
    if(!d)return;
    qdLog('WS-IN(8084) command='+d.command+' payload='+JSON.stringify(d));
    if(d.command==='quest_pause_ui')setQuestInteractiveVisible(d.visible===true,d.ready===true,d.pulse===true);
    else if(d.command==='set_quest_tab_state'){hasInteractive=d.hasInteractive===true;}
    else if(d.command==='set_quest_collapsed'){if(interactiveReady)applyCollapsed(d.collapsed,false,false);}
    else if(d.command==='quest_toggle_collapse'){if(interactiveReady){if(collapsed)expandWindow();else collapseWindow();}}
    else if(d.command==='quest_toggle_inventory')toggleInventory();
};
(function(){
    /* Кнопка сворачивания, закладка и клик по прозрачной области окна. */
    var btn=$('collapseBtn'),tab=$('questTab'),app=$('questApp');
    if(btn)btn.addEventListener('click',function(e){e.stopPropagation();collapseWindow()});
    if(tab)tab.addEventListener('click',function(e){e.stopPropagation();if(interactiveReady)expandWindow()});
    var itab=$('inventoryTab');if(itab)itab.addEventListener('click',function(e){e.stopPropagation();toggleInventory()});
    /* Оверлей полноэкранный, поэтому «прозрачная область окна квестов» — это сам
       контейнер #questApp вне панелей. Клик по ней сворачивает окно и выводит
       закладку «Квесты»; клики по содержимому окна всплывают от его элементов
       и не должны сворачивать окно. */
    if(app)app.addEventListener('click',function(e){
        if(collapsed)return;
        if(e.target!==app)return;
        collapseWindow();
    });
    /* Закладка выезжает за 220 мс, а её кликабельная область считается по
       текущему прямоугольнику. Пока анимация идёт, прямоугольник ещё смещён,
       поэтому область пересчитывается по завершении перехода. */
    if(tab)tab.addEventListener('transitionend',function(){if(collapsed)syncInput(false);publishInteractiveBounds()});
    window.addEventListener('resize',function(){if(collapsed)syncInput(false);publishInteractiveBounds()});
    var style=document.createElement('style');
    style.textContent='#interactionList .sideItem{position:relative;padding-left:9px;padding-right:52px}#interactionList .sideMain{display:inline-block;vertical-align:middle;max-width:145px}.sideDist{position:absolute;right:9px;top:50%;transform:translateY(-50%);color:#768497;font-size:10px}.questSectionTitle{padding:8px 10px 5px;color:#ffd45a;font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.6px}.questItem em{display:block;margin-top:5px;color:#8c9aad;font-size:10px;font-style:normal;line-height:1.35}.questStepDetail{margin-top:12px;padding:10px;border-left:2px solid #ffd21f;background:rgba(255,210,31,.05);color:#b9c2ce}.questRewardTitle{margin-top:18px;margin-bottom:5px;color:#ffd45a;font-weight:700}.rewardLine{padding:3px 0;font-weight:600}.dialogOption.quest-native-hover{border-color:rgba(255,211,77,.65);background:#333c49;box-shadow:inset 0 0 0 1px rgba(255,211,77,.18)}.sideItem.quest-native-hover,.questItem.quest-native-hover{background:rgba(255,205,85,.10)}.dialogOption{display:flex;flex-direction:column;gap:4px;align-items:flex-start}.optionReason{font-size:10px;color:#7e8a98;font-weight:400}.dialogTextRole{font-family:Roboto,"Roboto Regular","Segoe UI",Arial,sans-serif}.dialogTextService{font-family:"Courier New",Courier,monospace;color:rgba(255,255,255,.8);font-size:14px;margin-top:8px}.dialogTextService:before{content:""}.optionService{font-family:"Courier New",Courier,monospace;color:rgba(255,255,255,.8);font-size:12px}.optionRequirements{font-family:"Courier New",Courier,monospace;color:#0048ff;font-size:12px}.optionRequirements.unmet{color:#0048ff;opacity:.75}#dialogText.fading{opacity:0;transition:opacity 150ms ease}#dialogText{transition:opacity 150ms ease}.questWindow .panelTitle{font-size:13px}';
    document.head.appendChild(style);
})();

document.addEventListener('DOMContentLoaded',function(){
    clearDialogue();
    setTabPulse(false);
    setQuestInteractiveVisible(false,false,false);
    connect();
    /* Первый отчёт о геометрии input-окна (initial render). */
    publishInteractiveBounds();
    setTimeout(publishInteractiveBounds,120);
    setInterval(publishInteractiveBounds,1000);
});
})();