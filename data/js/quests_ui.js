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
var ws=null,model=null,currentQuest='',currentInteraction='',wasPaused=false,collapsed=true,questDetailPinned=false,inventoryOpen=false,archiveVisible=false,interactiveReady=false,activeInterface='none',selectedInventoryItem='';
var locallySeenInventoryItems=Object.create(null);
var pagePaused=false,hasInteractive=false,lastDialogueKey='',lastOptionsKey='',lastInteractionsKey='',lastQuestsKey='',typeTimer=null,fadeTimer=null;
var questBeaconVisible=false,inventoryBeaconVisible=false,questBeaconTimer=0,inventoryBeaconTimer=0;
var pendingQuestBeacon=false,pendingInventoryBeacon=false;
var inventoryBeaconSeen=Object.create(null),inventoryKnown=Object.create(null),inventoryKnownInitialized=false,lastNearbyInteractive=false;
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
    var mode='hidden',els=[];
    if(pagePaused){
        if(inventoryOpen){
            mode='inventory+quest-tab';els=[$('inventoryWindow'),$('questTab')];
        }else if(!collapsed){
            mode='quest-window+inventory-tab';els=[$('questWindow'),$('inventoryTab')];
        }else{
            mode='tabs';els=[$('questTab'),$('inventoryTab')];
        }
    }
    els=els.filter(Boolean);
    if(!els.length){
        post({command:'set_interactive_bounds',mode:'hidden',xr:0,yr:0,wr:0,hr:0});
        lastInteractiveKey='hidden';return;
    }
    var vw=Math.max(1,innerWidth),vh=Math.max(1,innerHeight);
    var left=Infinity,top=Infinity,right=-Infinity,bottom=-Infinity;
    els.forEach(function(el){
        var r=el.getBoundingClientRect();
        left=Math.min(left,r.left);top=Math.min(top,r.top);
        right=Math.max(right,r.right);bottom=Math.max(bottom,r.bottom);
    });
    var key=mode+'|'+Math.round(left)+'|'+Math.round(top)+'|'+Math.round(right-left)+'|'+Math.round(bottom-top);
    if(key===lastInteractiveKey)return;
    lastInteractiveKey=key;
    post({command:'set_interactive_bounds',mode:mode,
        xr:Math.max(0,Math.min(1,left/vw)),yr:Math.max(0,Math.min(1,top/vh)),
        wr:Math.max(0,Math.min(1,(right-left)/vw)),hr:Math.max(0,Math.min(1,(bottom-top)/vh))});
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
    if(activeInterface==='inventory'){
        var iw=$('inventoryWindow'),qt=$('questTab');
        return (!!iw&&(target===iw||iw.contains(target))) ||
               (!!qt&&(target===qt||qt.contains(target)));
    }
    if(activeInterface==='quest'){
        var qw=$('questWindow'),it=$('inventoryTab');
        return (!!qw&&(target===qw||qw.contains(target))) ||
               (!!it&&(target===it||it.contains(target)));
    }
    var qtab=$('questTab'),itab=$('inventoryTab');
    return (!!qtab&&(target===qtab||qtab.contains(target))) ||
           (!!itab&&(target===itab||itab.contains(target)));
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
        '<strong>SOFT CURSOR R13 1.0.40.83</strong>',
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
 * Прозрачность cursorDot НЕ зависит от alpha DOM-элементов.
 * Полностью видим над реальными блоками интерфейса; через 50 мс после
 * выхода за их пределы становится прозрачным.
 * ================================================================ */
var cursorEl=null,cursorTimer=null,cursorShown=false,cursorSystemReleased=false,cursorUiBlock=false,cursorHideTimer=null;

function isCursorOnUiBlock(x,y){
    if(!pagePaused)return false;
    try{
        /* Проверяем ВЕСЬ бокс изображения курсора, а не только его верхнюю точку.
           Координаты курсора — client X/Y, поэтому DOMRect уже в той же системе. */
        var cursorRect={
            left:Number(x)||0,
            top:Number(y)||0,
            right:(Number(x)||0)+27,
            bottom:(Number(y)||0)+44
        };
        var selectors=[
            '#questSidebar','#eventPanel','#responsePanel','#imagePanel','#servicePanel',
            '#inventoryWindow','#questTab','#inventoryTab',
            '.panelInner','.archiveToggle','.questItem','.sideItem','.inventoryItem',
            '.dialogOption'
        ];
        var nodes=document.querySelectorAll(selectors.join(','));
        for(var i=0;i<nodes.length;i++){
            var node=nodes[i];
            if(!node)continue;
            var cs=window.getComputedStyle(node);
            if(cs.display==='none'||cs.visibility==='hidden')continue;
            var rect=node.getBoundingClientRect();
            if(rect.width<=0||rect.height<=0)continue;
            if(cursorRect.left<rect.right&&cursorRect.right>rect.left&&
               cursorRect.top<rect.bottom&&cursorRect.bottom>rect.top)return true;
        }
        return false;
    }catch(e){return false}
}
function cancelCursorHideTimer(){
    if(cursorHideTimer){clearTimeout(cursorHideTimer);cursorHideTimer=null}
}
function scheduleCursorHide(){
    cancelCursorHideTimer();
    cursorHideTimer=setTimeout(function(){
        cursorHideTimer=null;
        if(pagePaused&&!cursorUiBlock&&cursorEl){
            cursorEl.style.opacity='0';
        }
    },50);
}
function placeCursor(x,y){
    cursorEl=cursorEl||$('cursorDot');   // резолвим лениво: place может вызваться первым
    if(!cursorEl)return;

    cursorEl.style.transform='translate('+x+'px,'+y+'px)';
    var onBlock=isCursorOnUiBlock(x,y);

    if(onBlock){
        cursorUiBlock=true;
        cancelCursorHideTimer();
        cursorEl.style.opacity='1';
    }else{
        if(cursorUiBlock){
            cursorUiBlock=false;
            scheduleCursorHide();
        }else if(!cursorShown){
            scheduleCursorHide();
        }
    }

    if(!cursorShown){
        cursorShown=true;
        cursorEl.style.display='block';
    }

    qdCounters.place++;
    qdLastPlace.x=x;qdLastPlace.y=y;qdLastPlace.shown=!!(cursorEl&&cursorEl.style.display==='block');
    var now=Date.now();
    if(qdCounters.place<=5||now-qdPlaceLogAt>=400){
        qdPlaceLogAt=now;
        qdLog('[CURSOR-PLACE] x='+x+' y='+y+' onUiBlock='+onBlock+' shown='+qdLastPlace.shown+' placeCount='+qdCounters.place);
    }
}

function trackCursorFromEvent(e){
    /* Курсор живёт во всём интерактивном режиме, включая свернутые закладки.
       Вне визуальных блоков он исчезает с задержкой ровно 50 мс. */
    if(!pagePaused)return;
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
    cancelCursorHideTimer();
    cursorUiBlock=false;
    cursorEl.style.opacity='0';
    cursorEl.style.display='block';
    cursorShown=true;
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
    cancelCursorHideTimer();
    qnSetHoverTarget(null);
    qnPressedTarget=null;
    cursorUiBlock=false;
    if(cursorEl){cursorEl.style.display='none';cursorEl.style.opacity='0';cursorShown=false}
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
    /* Курсор нужен только над открытым интерфейсом либо над видимой закладкой.
       В режиме «обе закладки» он скрыт по всему экрану и появляется только
       когда весь бокс 27x44 px пересекает закладку. */
    var app=$('questApp');
    if(app)app.style.cursor=pagePaused?'none':'';

    var tabVisible=
        !!(($('questTab')&&$('questTab').classList.contains('visible')) ||
           ($('inventoryTab')&&$('inventoryTab').classList.contains('visible')));
    var active=pagePaused && (activeInterface!=='none' || tabVisible);

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
/* ================================================================ ИНТЕРФЕЙСЫ
 * Единый state machine для всех интерактивных экранов.
 * Состояния:
 *   none      — оба интерфейса закрыты; обе закладки видимы в паузе;
 *   quest     — открыты Квесты; уезжает только закладка Квестов;
 *   inventory — открыт Инвентарь; уезжает только закладка Инвентаря.
 * Вне паузы интерфейсы закрыты, а закладки появляются только как
 * одноразовые двухимпульсные маяки.
 * ================================================================ */
function clearBookmarkBeacon(kind){
    var isQuest=kind==='quest',tab=$(isQuest?'questTab':'inventoryTab');
    if(isQuest){
        if(questBeaconTimer){clearTimeout(questBeaconTimer);questBeaconTimer=0}
        questBeaconVisible=false;pendingQuestBeacon=false;
    }else{
        if(inventoryBeaconTimer){clearTimeout(inventoryBeaconTimer);inventoryBeaconTimer=0}
        inventoryBeaconVisible=false;pendingInventoryBeacon=false;
    }
    if(tab)tab.classList.remove('beacon');
    syncTabVisibility();
}
function startBookmarkBeacon(kind){
    var isQuest=kind==='quest';
    if(pagePaused){
        if(isQuest)pendingQuestBeacon=true;else pendingInventoryBeacon=true;
        return;
    }
    if(isQuest){
        if(activeInterface==='quest'||questBeaconVisible)return;
        pendingQuestBeacon=false;questBeaconVisible=true;
        if(questBeaconTimer)clearTimeout(questBeaconTimer);
        questBeaconTimer=setTimeout(function(){clearBookmarkBeacon('quest')},1550);
        var qt=$('questTab');
        if(qt){qt.classList.remove('beacon');void qt.offsetWidth;qt.classList.add('beacon')}
    }else{
        if(activeInterface==='inventory'||inventoryBeaconVisible)return;
        pendingInventoryBeacon=false;inventoryBeaconVisible=true;
        if(inventoryBeaconTimer)clearTimeout(inventoryBeaconTimer);
        inventoryBeaconTimer=setTimeout(function(){clearBookmarkBeacon('inventory')},1550);
        (model&&model.inventory||[]).forEach(function(x){
            var id=String(x&&x.id||'');
            if(id&&x.new_item===true)inventoryBeaconSeen[id]=true;
        });
        var it=$('inventoryTab');
        if(it){it.classList.remove('beacon');void it.offsetWidth;it.classList.add('beacon')}
    }
    syncTabVisibility();
}
function flushPendingBookmarkBeacons(){
    if(pagePaused)return;
    if(pendingQuestBeacon)startBookmarkBeacon('quest');
    if(pendingInventoryBeacon)startBookmarkBeacon('inventory');
}
function syncTabVisibility(){
    var qt=$('questTab'),it=$('inventoryTab');
    var qVisible=pagePaused ? activeInterface!=='quest' : questBeaconVisible;
    var iVisible=pagePaused ? activeInterface!=='inventory' : inventoryBeaconVisible;
    if(qt){
        qt.classList.toggle('visible',qVisible);
        qt.classList.toggle('interactive',qVisible&&pagePaused);
        qt.classList.remove('active');
    }
    if(it){
        it.classList.toggle('visible',iVisible);
        it.classList.toggle('interactive',iVisible&&pagePaused);
        it.classList.remove('active');
    }
}
function playInterfaceMotion(el,entering){
    if(!el)return;
    el.classList.remove('interfaceMotionEnter','interfaceMotionExit');
    void el.offsetWidth;
    el.classList.add(entering?'interfaceMotionEnter':'interfaceMotionExit');
}

function syncInterfaceState(name,notify,report){
    if(name!=='quest'&&name!=='inventory')name='none';
    var previousInterface=activeInterface;
    activeInterface=name;
    inventoryOpen=name==='inventory';
    collapsed=name!=='quest';
    qdStateChanged();

    var qw=$('questWindow'),iw=$('inventoryWindow');
    if(qw){
        var questWasActive=previousInterface==='quest';
        var questNowActive=name==='quest';
        qw.classList.toggle('collapsed',!questNowActive);
        qw.classList.toggle('interfaceActive',questNowActive);
        if(questWasActive!==questNowActive)playInterfaceMotion(qw,questNowActive);
    }
    if(iw){
        var inventoryWasActive=previousInterface==='inventory';
        var inventoryNowActive=name==='inventory';
        iw.classList.toggle('interfaceActive',inventoryNowActive);
        if(inventoryWasActive!==inventoryNowActive)playInterfaceMotion(iw,inventoryNowActive);
    }

    // При открытии уезжает только собственная закладка.
    if(name==='quest'){
        if(questBeaconTimer){clearTimeout(questBeaconTimer);questBeaconTimer=0}
        questBeaconVisible=false;var qt=$('questTab');if(qt)qt.classList.remove('beacon');
    }else if(name==='inventory'){
        if(inventoryBeaconTimer){clearTimeout(inventoryBeaconTimer);inventoryBeaconTimer=0}
        inventoryBeaconVisible=false;var it=$('inventoryTab');if(it)it.classList.remove('beacon');
    }
    syncTabVisibility();
    applyCursorLayer();
    /* Открытие интерфейса сразу перестраивает его контент из последнего
       полученного quest_state, не ожидая следующего телеметрического тика. */
    if(model){
        renderQuests();
        renderInventory();
    }
    publishInteractiveBounds();
    if(report)send({command:'quest_window_state',collapsed:name!=='quest'});
    if(notify)post({command:'return_focus'});
}
function toggleInterface(name){
    if(!pagePaused||!interactiveReady||blockToggle())return;
    var current=activeInterface;
    var next=current===name?'none':name;
    var reportQuestState=(name==='quest'||current==='quest');
    syncInterfaceState(next,false,reportQuestState);
}
function toggleQuestInterface(){toggleInterface('quest')}
function toggleInventory(){toggleInterface('inventory')}
function collapseWindow(){
    if(activeInterface==='quest')toggleQuestInterface();
    else if(activeInterface==='inventory')toggleInventory();
}
function expandWindow(){if(activeInterface!=='quest')syncInterfaceState('quest',false,true)}
function blockToggle(){
    var now=Date.now();
    if(now-lastToggleAt<250)return true;
    lastToggleAt=now;
    return false;
}

function setTabPulse(value){
    hasInteractive=!!value;
    syncTabVisibility();
}

/* ------------------------------------------------------------ набор текста */
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
    (model.nearby||[]).forEach(function(p){
        if(p&&p.QuestId&&p.Marker&&p.Marker!=='none'&&!seen[p.QuestId]){
            seen[p.QuestId]=true;nearIds.push(p.QuestId);
        }
    });

    var available=model.availableQuests||[];
    var active=model.activeQuests||[];
    var archive=model.archiveQuests||[];
    var all=available.concat(active,archive),byId={};
    all.forEach(function(q){byId[q.id]=q});

    var nearby=nearIds.map(function(id){return byId[id]}).filter(Boolean);
    var activeNotNearby=active.filter(function(q){return !seen[q.id]});
    var availableNotNearby=available.filter(function(q){return !seen[q.id]});

    var key=JSON.stringify([
        currentQuest,currentInteraction,archiveVisible,
        nearby.map(function(q){return[q.id,q.status,q.stepDescription]}),
        availableNotNearby.map(function(q){return[q.id,q.status,q.stepDescription]}),
        activeNotNearby.map(function(q){return[q.id,q.status,q.stepDescription]}),
        archive.map(function(q){return[q.id,q.status,q.stepDescription]})
    ]);
    if(key===lastQuestsKey)return;
    lastQuestsKey=key;

    var html='';
    if(nearby.length){
        html+='<div class="questSectionTitle">Рядом</div>'+
            nearby.map(function(q){return questButton(q,'nearby')}).join('')+
            '<div class="questDivider"></div>';
    }
    if(availableNotNearby.length){
        html+='<div class="questSectionTitle">Доступные</div>'+
            availableNotNearby.map(function(q){return questButton(q,'available')}).join('');
    }
    if(activeNotNearby.length){
        html+='<div class="questSectionTitle">Активные</div>'+
            activeNotNearby.map(function(q){return questButton(q,'active')}).join('');
    }

    html+='<button id="archiveToggle" class="archiveToggle" type="button">'+
        (archiveVisible?'архив ▲':'архив ▼')+'</button>';

    if(archiveVisible){
        html+='<div class="questDivider"></div><div class="questSectionTitle">Архив</div>'+
            archive.map(function(q){return questButton(q,'archive')}).join('');
    }

    el.innerHTML=html||'<div class="muted">Нет квестов</div>';

    el.querySelectorAll('.questItem').forEach(function(btn){
        btn.onclick=function(e){
            e.stopPropagation();
            var near=firstNearbyForQuest(btn.dataset.q);
            if(near)selectInteraction(near.QuestId,near.InteractionId);
            else showQuestDetail(btn.dataset.q);
        };
    });

    var ab=$('archiveToggle');
    if(ab)ab.onclick=function(e){
        e.stopPropagation();
        archiveVisible=!archiveVisible;
        lastQuestsKey='';
        renderQuests();
    };
}
function questButton(q,kind){var selected=questDetailPinned&&!currentInteraction&&currentQuest===q.id;return'<button class="questItem '+kind+(selected?' selected':'')+'" data-q="'+esc(q.id)+'"><strong>'+esc(q.title)+'</strong><span>'+esc(q.status||'')+'</span>'+((q.stepDescription||q.description)?'<em>'+esc(q.stepDescription||q.description)+'</em>':'')+'</button>'}
function inventoryHasNewItems(){
    return !!((model&&model.inventory)||[]).some(function(x){
        return x&&x.new_item===true&&!locallySeenInventoryItems[String(x.id||'')];
    });
}
function setInventoryTabPulse(on){syncTabVisibility()}
function renderInventory(){
    var el=$('inventoryList');if(!el||!model)return;
    var items=model.inventory||[];
    items.forEach(function(x){
        var id=String(x&&x.id||'');
        if(x&&x.new_item!==true)delete locallySeenInventoryItems[id];
    });
    var currentId=selectedInventoryItem;
    if(currentId && !items.some(function(x){return String(x&&x.id||'')===currentId}))selectedInventoryItem='';
    currentId=selectedInventoryItem;
    el.innerHTML=items.map(function(x){
        var id=String(x&&x.id||'');
        var isNew=x&&x.new_item===true&&!locallySeenInventoryItems[id];
        var selected=id===currentId;
        return'<button class="inventoryItem'+(selected?' selected':'')+'" data-item="'+esc(id)+'">'
            +'<span class="inventoryItemName">'
            +'<span class="inventoryItemLabel">'+esc(x.name||id)+'</span>'
            +(isNew?'<span class="inventoryNewDot" aria-hidden="true">*</span>':'')
            +'</span><small>×'+esc(x.amount||1)+'</small></button>';
    }).join('')||'<div class="muted">пусто</div>';
    setInventoryTabPulse(inventoryHasNewItems()&&activeInterface==='none');
    el.querySelectorAll('.inventoryItem').forEach(function(btn){
        btn.onclick=function(e){
            e.stopPropagation();
            var id=String(btn.dataset.item||'');
            if(!id)return;
            selectedInventoryItem=id;
            var item=(model.inventory||[]).find(function(x){return String(x&&x.id||'')===id});
            if(item&&item.new_item===true){
                locallySeenInventoryItems[id]=true;
                send({command:'inventory_item_seen',id:id});
            }
            renderInventory();
        };
    });
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

function setQuestInteractiveVisible(visible,ready,pulse){
    pagePaused=!!visible;interactiveReady=!!ready;
    var app=$('questApp');if(app)app.classList.toggle('interactiveVisible',pagePaused);
    if(!pagePaused){
        archiveVisible=false;currentQuest='';currentInteraction='';questDetailPinned=false;
        clearDialogue();stopCursorTrack('interactive-hidden');

        /* Всё, что уже было показано в паузе, обязано исчезнуть.
           Маяки, накопленные ВО ВРЕМЯ паузы, сохраняем и запускаем
           только после очистки старого состояния. */
        var deferredQuestBeacon=pendingQuestBeacon;
        var deferredInventoryBeacon=pendingInventoryBeacon;
        clearBookmarkBeacon('quest');
        clearBookmarkBeacon('inventory');

        syncInterfaceState('none',false,false);
        if(deferredQuestBeacon)startBookmarkBeacon('quest');
        if(deferredInventoryBeacon)startBookmarkBeacon('inventory');
    }else{
        syncInterfaceState('none',false,false);
        setTabPulse(pulse===true);
        /* Содержимое строим сразу, не ожидая отдельного «правильного»
           порядка quest_pause_ui/quest_state. Если model уже получена,
           пользователь видит данные без дополнительного тика WS. */
        renderQuests();
        renderInventory();
    }
    publishInteractiveBounds();
}
function applyState(data){
    if(!$('questApp'))return;
    model=data;

    var pausedByState=data.paused===true;
    var nearbyNow=!!((data.nearby)||[]).some(function(p){return p&&p.Marker&&p.Marker!=='none'});

    /* Если backend уже подтвердил паузу, квестовый beacon не должен
       появиться поверх AR до прихода quest_pause_ui. Старый beacon здесь
       тоже немедленно убираем. */
    if(pausedByState){
        if(questBeaconVisible){
            if(questBeaconTimer){clearTimeout(questBeaconTimer);questBeaconTimer=0}
            questBeaconVisible=false;
            var qbt=$('questTab');if(qbt)qbt.classList.remove('beacon');
        }
        if(inventoryBeaconVisible){
            if(inventoryBeaconTimer){clearTimeout(inventoryBeaconTimer);inventoryBeaconTimer=0}
            inventoryBeaconVisible=false;
            var ibt=$('inventoryTab');if(ibt)ibt.classList.remove('beacon');
        }
    }else if(nearbyNow&&!lastNearbyInteractive&&!pagePaused){
        startBookmarkBeacon('quest');
    }
    lastNearbyInteractive=nearbyNow;

    var presentInventory=Object.create(null);
    var currentInventory=Object.create(null);
    var inventoryReady=!inventoryKnownInitialized;

    (data.inventory||[]).forEach(function(x){
        var id=String(x&&x.id||'');
        if(!id)return;

        var amount=Number(x&&x.amount||0);
        if(!Number.isFinite(amount))amount=0;

        presentInventory[id]=true;
        currentInventory[id]=amount;

        /* Beacon только при ФАКТИЧЕСКОМ появлении/увеличении предмета
           после уже полученного базового снимка. Стартовая загрузка и
           повторные quest_state с тем же new_item beacon не запускают. */
        var previousAmount=inventoryKnown[id];
        var appeared=inventoryKnownInitialized &&
            (previousAmount===undefined || amount>previousAmount);

        if(appeared && x.new_item===true && !inventoryBeaconSeen[id]){
            if(pagePaused){
                pendingInventoryBeacon=true;
            }else{
                inventoryBeaconSeen[id]=true;
                startBookmarkBeacon('inventory');
            }
        }
    });

    Object.keys(inventoryKnown).forEach(function(id){
        if(!presentInventory[id])delete inventoryKnown[id];
    });
    Object.keys(inventoryBeaconSeen).forEach(function(id){
        if(!presentInventory[id])delete inventoryBeaconSeen[id];
    });

    inventoryKnown=currentInventory;
    inventoryKnownInitialized=true;
    setInventoryTabPulse(inventoryHasNewItems());
    var paused=data.paused===true,interactive=data.interactive===true;
    /* interactive из quest_state — пост-валидатор. Видимость UI меняется
       только явной командой quest_pause_ui от приложения. */
    var stateKey=interactive+'|'+paused+'|'+(data.selectedQuest||'')+'|'+(data.selectedInteraction||'')+'|'+(data.dialogue?'1':'0');
    if(stateKey!==qdLastQuestStateKey){qdLastQuestStateKey=stateKey;qdLog('WS-IN(8085) quest_state paused='+paused+' interactive='+interactive+' selected='+(data.selectedInteraction||''))}
    /* Рендер не зависит от флага interactive в конкретном пакете:
       backend может прислать состояние на границе перехода паузы.
       Само отображение всё равно контролирует quest_pause_ui/category. */
    renderQuests();
    renderInventory();
    if(!interactive){
        publishInteractiveBounds();
        return;
    }
    if(questDetailPinned){
        /* Локально открытая карточка квеста не должна заменяться backend-blank
           состоянием: у карточки нет selectedInteraction по протоколу. */
        currentInteraction='';
    }else{
        /* Backend selection теперь передаётся КАЖДЫМ quest_state, поэтому
           можно безопасно считать его авторитетным состоянием выбора. */
        currentQuest=data.selectedQuest||'';
        currentInteraction=data.selectedInteraction||'';
    }
    if(data.dialogue&&!questDetailPinned)renderDialogue(data.dialogue);
    else if(!questDetailPinned&&!data.selectedQuest&&!data.selectedInteraction)clearDialogue();
    renderQuests();renderInventory();publishInteractiveBounds();
}
var stateRequestTimer=0,stateRequestAttempts=0;
function requestQuestState(){
    if(stateRequestAttempts>=8)return;
    stateRequestAttempts++;
    send({command:'quest_state_request'});
    if(stateRequestTimer)clearTimeout(stateRequestTimer);
    stateRequestTimer=setTimeout(requestQuestState,500);
}
function connect(){try{ws=new WebSocket('ws://localhost:8085/');ws.onmessage=function(ev){try{var d=JSON.parse(ev.data);if(d.command==='quest_state')applyState(d);else if(d.command==='quest_error')showError(d.text)}catch(e){}};ws.onclose=function(){setTimeout(connect,1500)};ws.onerror=function(){try{ws.close()}catch(e){}}}catch(e){setTimeout(connect,1500)}}
function showError(text){var e=$('overlayError');if(!e)return;e.textContent=text||'Ошибка';e.classList.add('show');setTimeout(function(){e.classList.remove('show')},2500)}

/* Команды приложения, адресованные именно окну квестов. */
window.onEts2Command=function(d){
    if(!d)return;
    qdLog('WS-IN(8084) command='+d.command+' payload='+JSON.stringify(d));
    if(d.command==='quest_pause_ui'){
        setQuestInteractiveVisible(d.visible===true,d.ready===true,d.pulse===true);
        if(d.hasInteractive!==undefined)setTabPulse(d.hasInteractive===true);
    }
    else if(d.command==='set_quest_tab_state'){setTabPulse(d.hasInteractive===true);}
    else if(d.command==='quest_bookmark_beacon'){startBookmarkBeacon('quest');}
    else if(d.command==='inventory_bookmark_beacon'){startBookmarkBeacon('inventory');}
    else if(d.command==='quest_collapse_interfaces'){
        if(pagePaused)syncInterfaceState('none',false,false);
    }
    else if(d.command==='set_quest_collapsed'){
        // Пока открыт Инвентарь, Квесты обязаны оставаться закрытыми.
        // Это защищает от echo-команды C# после quest_window_state(collapsed=true),
        // которая иначе могла бы немедленно закрыть уже открытый Инвентарь.
        if(interactiveReady && !inventoryOpen)
            syncInterfaceState(d.collapsed?'none':'quest',false,false);
    }
    else if(d.command==='quest_toggle_collapse')toggleQuestInterface();
    else if(d.command==='quest_toggle_inventory')toggleInventory();
};
(function(){
    /* Кнопка сворачивания, закладка и клик по прозрачной области окна. */
    var btn=$('collapseBtn'),tab=$('questTab'),app=$('questApp');
    if(btn)btn.addEventListener('click',function(e){e.stopPropagation();collapseWindow()});
    if(tab)tab.addEventListener('click',function(e){
        e.stopPropagation();
        toggleQuestInterface();
    });
    var itab=$('inventoryTab');if(itab)itab.addEventListener('click',function(e){e.stopPropagation();toggleInventory()});
    /* Оверлей полноэкранный, поэтому «прозрачная область окна квестов» — это сам
       контейнер #questApp вне панелей. Клик по ней сворачивает окно и выводит
       закладку «Квесты»; клики по содержимому окна всплывают от его элементов
       и не должны сворачивать окно. */
    if(app)app.addEventListener('click',function(e){
        if(!pagePaused||activeInterface==='none')return;
        var t=e.target,insideManagedTarget=false;
        try{
            insideManagedTarget=!!(t&&t.closest&&t.closest(
                '#questWindow,#inventoryWindow,#questTab,#inventoryTab'
            ));
        }catch(_){}
        if(!insideManagedTarget)collapseWindow();
    });
    if(tab)tab.addEventListener('transitionend',function(){syncTabVisibility();if(pagePaused)syncInput(false);publishInteractiveBounds()});
    if(itab)itab.addEventListener('transitionend',function(){syncTabVisibility();if(pagePaused)syncInput(false);publishInteractiveBounds()});
    window.addEventListener('resize',function(){if(collapsed)syncInput(false);publishInteractiveBounds()});
    var style=document.createElement('style');
    style.textContent='#interactionList .sideItem{position:relative;padding-left:9px;padding-right:52px}.#interactionList .sideMain{display:inline-block;vertical-align:middle;max-width:145px}.sideDist{position:absolute;right:9px;top:50%;transform:translateY(-50%);color:#768497;font-size:10px}.questSectionTitle{padding:8px 10px 5px;color:#ffd45a;font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.6px}.questItem em{display:block;margin-top:5px;color:#8c9aad;font-size:10px;font-style:normal;line-height:1.35}.questStepDetail{margin-top:12px;padding:10px;border-left:2px solid #ffd21f;background:rgba(255,210,31,.05);color:#b9c2ce}.questRewardTitle{margin-top:18px;margin-bottom:5px;color:#ffd45a;font-weight:700}.rewardLine{padding:3px 0;font-weight:600}.dialogOption{display:flex;flex-direction:column;gap:4px;align-items:flex-start}#questApp button{box-sizing:border-box;border:1px solid rgba(255,255,255,.12);background:rgb(19,20,21);color:#e7edf4;transition:background-color 50ms ease,border-color 50ms ease,box-shadow 50ms ease,color 50ms ease}#questApp button:hover,#questApp button.quest-native-hover{border-color:rgba(255,211,77,.65);background:rgb(33,34,35)}#questApp button:disabled{opacity:.38;cursor:not-allowed}#questApp #questTab,#questApp #inventoryTab{border-left:0;border-color:rgba(255,255,255,.12);background:rgb(19,20,21)}#questApp #questTab:hover,#questApp #inventoryTab:hover,#questApp #questTab.quest-native-hover,#questApp #inventoryTab.quest-native-hover{border-color:rgba(255,211,77,.65);border-left:0;background:rgb(33,34,35)}#questApp .questItem.selected,#questApp .inventoryItem.selected,#questApp .sideItem.selected{border-color:rgba(255,211,77,.65);background:rgb(33,34,35)}.optionReason{font-size:10px;color:#7e8a98;font-weight:400}.dialogTextRole{font-family:Roboto,"Roboto Regular","Segoe UI",Arial,sans-serif}.dialogTextService{font-family:"Courier New",Courier,monospace;color:rgba(255,255,255,.8);font-size:14px;margin-top:8px}.dialogTextService:before{content:""}.optionService{font-family:"Courier New",Courier,monospace;color:rgba(255,255,255,.8);font-size:12px}.optionRequirements{font-family:"Courier New",Courier,monospace;color:#0048ff;font-size:12px}.optionRequirements.unmet{color:#0048ff;opacity:.75}#dialogText.fading{opacity:0;transition:opacity 150ms ease}#dialogText{transition:opacity 150ms ease}.questWindow .panelTitle{font-size:13px}';
    document.head.appendChild(style);
})();

document.addEventListener('DOMContentLoaded',function(){
    clearDialogue();
    setTabPulse(false);
    setQuestInteractiveVisible(false,false,false);
    connect();
    setTimeout(requestQuestState,100);
    /* Первый отчёт о геометрии input-окна (initial render). */
    publishInteractiveBounds();
    setInterval(publishInteractiveBounds,1000);
});
})();