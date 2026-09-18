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
var ws=null,model=null,currentQuest='',currentInteraction='',wasPaused=false,collapsed=false;
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
var qdCounters={mouseMove:0,mouseDown:0,mouseUp:0,click:0,place:0,posts:0,wsOut:0,cursorStart:0,cursorStop:0};
var qdLastMove={x:null,y:null,target:'',at:0};
var qdLastPlace={x:null,y:null,shown:false};
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
        lastX:qdLastMove.x===null?-1:qdLastMove.x,lastY:qdLastMove.y===null?-1:qdLastMove.y,lastTarget:qdLastMove.target,
        paused:pagePaused,collapsed:collapsed,
        cursorDotShown:!!(cursorEl&&cursorEl.style.display==='block'),
        cursorDotX:qdLastPlace.x===null?-1:qdLastPlace.x,cursorDotY:qdLastPlace.y===null?-1:qdLastPlace.y,
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
    qdCounters.posts++;qdLog('POST '+qdCommandText(o)+' sent='+sent);
}
function setNativeClickable(value){post({command:'set_clickable',value:!!value})}

/* ================================================================ КУРСОР
 * v1.0.40.61: СОБСТВЕННЫЙ КУРСОР СТРАНИЦЫ.
 *
 * ETS2 прячет системный курсор и рисует свой прямо в ИГРОВОЙ КАДР. Игровой кадр
 * лежит НИЖЕ окна оверлея, поэтому стрелка игры видна и двигается «под окном»:
 * ни ShowCursor, ни SetCursor, ни WM_SETCURSOR это не исправят — наш слой в
 * любом случае выше. К тому же игра уводит счётчик ShowCursor глубоко в минус.
 *
 * Поэтому стрелку рисуем САМИ, внутри страницы: #cursorDot — обычный SVG,
 * позиционируемый по координатам мыши. Он часть нашей разметки, значит всегда
 * выше и игрового кадра, и любого системного курсора.
 * Позиция берётся из события движения мыши (окно оверлея мышь принимает, пока
 * окно развёрнуто), плюс опрос на случай, если событие не пришло.
 * ================================================================ */
var cursorEl=null,cursorTimer=null,cursorShown=false;

function placeCursor(x,y){
    cursorEl=cursorEl||$('cursorDot');   // резолвим лениво: place может вызваться первым
    if(!cursorEl)return;
    cursorEl.style.transform='translate('+x+'px,'+y+'px)';
    if(!cursorShown){cursorShown=true;cursorEl.style.display='block'}
    /* ДИАГНОСТИКА: первые 5 вызовов, затем не чаще 2-3 раз в секунду. */
    qdCounters.place++;
    qdLastPlace.x=x;qdLastPlace.y=y;qdLastPlace.shown=!!(cursorEl&&cursorEl.style.display==='block');
    var now=Date.now();
    if(qdCounters.place<=5||now-qdPlaceLogAt>=400){
        qdPlaceLogAt=now;
        qdLog('[CURSOR-PLACE] x='+x+' y='+y+' shown='+qdLastPlace.shown+' placeCount='+qdCounters.place);
    }
}

function trackCursorFromEvent(e){
    if(!pagePaused||collapsed)return;
    placeCursor(e.clientX,e.clientY);
}

function startCursorTrack(){
    cursorEl=cursorEl||$('cursorDot');
    qdCounters.cursorStart++;
    /* Логируем ПЕРЕХОДЫ (иначе syncInput раз в секунду давал бы поток строк). */
    if(!qdTracking){
        qdTracking=true;
        qdLog('[CURSOR] start pagePaused='+pagePaused+' collapsed='+collapsed+' cursorElementExists='+!!cursorEl+' startCount='+qdCounters.cursorStart);
    }
    if(!cursorEl)return;
    window.addEventListener('mousemove',trackCursorFromEvent);
    window.addEventListener('mouseover',trackCursorFromEvent);
    if(!cursorTimer)cursorTimer=setInterval(function(){
        if(!pagePaused||collapsed){stopCursorTrack('keepalive:paused-or-collapsed');return}
        /* Событие движения может не прийти (курсор уже стоит на месте) —
           поэтому начальную позицию берём один раз принудительно. */
        if(!cursorShown)placeCursor(window.innerWidth/2,window.innerHeight/2);
    },120);
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
    if(cursorEl){cursorEl.style.display='none';cursorShown=false}
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
       СОБСТВЕННУЮ стрелку (<svg id="cursorDot">) по позиции мыши — она часть
       нашей страницы, значит гарантированно выше игровой.
       Скрытие системной стрелки оставлено как дополнительная мера: если игра
       её не рисует, она не будет дублировать нашу. */
    var app=$('questApp');
    if(app)app.style.cursor=pagePaused?'none':'';
    var active=pagePaused&&!collapsed;
    if(active)startCursorTrack();else stopCursorTrack('applyCursorLayer:inactive');
}

/* Мышь окна управляется из двух состояний: активна ли пауза и свёрнуто ли окно.
   Развёрнутое окно кликабельно целиком; свёрнутое отдаёт мыши только область
   закладки у левой границы экрана; скрытое окно прозрачно для мыши. */
function syncInput(notify){
    qdLog('syncInput '+qdStateText());
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
    var speaker=$('dialogSpeaker'),text=$('dialogText'),img=$('dialogImage'),opts=$('dialogOptions');
    if(speaker)speaker.textContent=node.speaker||'';
    var key=(node.speaker||'')+'|'+(node.text||'')+'|'+(node.serviceText||'');
    var changed=key!==lastDialogueKey;
    lastDialogueKey=key;
    if(text){
        if(changed&&text.textContent&&text.textContent.length>1){
            text.classList.add('fading');
            stopTyping();
            fadeTimer=setTimeout(function(){text.classList.remove('fading');typeInto(text,node.text||'',node.serviceText||'')},150);
        }else if(changed){
            typeInto(text,node.text||'',node.serviceText||'');
        }
    }
    if(img){if(node.image){img.src=node.image;img.style.display='block';img.parentElement&&img.parentElement.classList.remove('empty')}else{img.removeAttribute('src');img.style.display='none';img.parentElement&&img.parentElement.classList.add('empty')}}
    if(opts){
        /* Состояние приходит примерно раз в секунду, поэтому список ответов
           перестраиваем только при реальном изменении — иначе кнопки теряют
           подсветку под курсором и клик может не попасть. */
        var optionsKey=JSON.stringify((node.options||[]).map(function(o){return[o.id,o.text,o.serviceText,o.requirements,o.requirementsMet,o.enabled,o.reason]}));
        if(optionsKey!==lastOptionsKey){
            lastOptionsKey=optionsKey;
            opts.innerHTML=(node.options||[]).map(function(o,i){
                var req=o.requirements?'<small class="optionRequirements'+(o.requirementsMet===false?' unmet':'')+'">'+esc(o.requirements)+'</small>':'';
                var svc=o.serviceText?'<small class="optionService">'+esc(o.serviceText)+'</small>':'';
                var reason=o.enabled===false?'<small class="optionReason">'+esc(o.reason||'Требование не выполнено')+'</small>':'';
                return'<button class="dialogOption" data-index="'+i+'" '+(o.enabled===false?'disabled':'')+'><span class="optionText">'+esc(o.text)+'</span>'+svc+req+reason+'</button>';
            }).join('');
            opts.querySelectorAll('.dialogOption').forEach(function(b){b.onclick=function(){send({command:'quest_dialog_option',questId:currentQuest,interaction:currentInteraction,index:Number(b.dataset.index)})}});
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
function renderQuests(){
    var el=$('questList');if(!el||!model)return;
    var active=model.activeQuests||[],archive=model.archiveQuests||[];
    var key=JSON.stringify(active.concat(archive).map(function(q){return[q.id,q.title,q.status,q.stepDescription,q.description]}));
    if(key===lastQuestsKey)return;
    lastQuestsKey=key;
    var html='';
    if(active.length){html+='<div class="questSectionTitle">Активные</div>'+active.map(function(q){return questButton(q,true)}).join('')}
    if(archive.length){html+='<div class="questSectionTitle">Архив</div>'+archive.map(function(q){return questButton(q,false)}).join('')}
    el.innerHTML=html||'<div class="muted">Нет квестов</div>';
    el.querySelectorAll('.questItem').forEach(function(b){b.onclick=function(){showQuestDetail(b.dataset.q)}})
}
function questButton(q,active){return'<button class="questItem '+(active?'active':'archive')+'" data-q="'+esc(q.id)+'"><strong>'+esc(q.title)+'</strong><span>'+esc(q.status||'')+'</span>'+((q.stepDescription||q.description)?'<em>'+esc(q.stepDescription||q.description)+'</em>':'')+'</button>'}
function showQuestDetail(id){
    var q=[].concat((model&&model.activeQuests)||[],(model&&model.archiveQuests)||[]).find(function(x){return x.id===id});if(!q)return;
    currentQuest=id;currentInteraction='';
    var speaker=$('dialogSpeaker'),text=$('dialogText'),opts=$('dialogOptions'),img=$('dialogImage');
    if(speaker)speaker.textContent=q.title;
    if(text)text.innerHTML='<span class="dialogTextRole">'+esc(q.description||'')+'</span>'+(q.stepDescription?'<div class="dialogTextService">'+esc(q.stepDescription)+'</div>':'')+'<div class="questRewardTitle">Награды</div>'+((q.rewards||[]).map(function(r){var col=r.color?' style="color:'+esc(r.color)+'"':'';return'<div class="rewardLine"'+col+'>'+esc(r.display||r.id)+' x'+esc(r.amount||1)+'</div>'+(r.serviceText?'<div class="dialogTextService">'+esc(r.serviceText)+'</div>':'')}).join('')||'<div class="muted">—</div>');
    if(opts)opts.innerHTML='';if(img){img.removeAttribute('src');img.style.display='none'}
    lastDialogueKey='';lastOptionsKey='';
    renderInteractions();
}
function renderInventory(){
    var el=$('inventory');if(!el||!model)return;var items=model.inventory||[];
    el.innerHTML='<span class="inventoryTitle">Инвентарь</span> '+(items.length?items.map(function(x){return'<span class="inventoryItem">'+esc(x.name||x.id)+' ×'+esc(x.amount||0)+'</span>'}).join(' '):'<span class="muted">пусто</span>');
}
function clearDialogue(){stopTyping();lastDialogueKey='';lastOptionsKey='';var s=$('dialogSpeaker'),t=$('dialogText'),o=$('dialogOptions'),i=$('dialogImage');if(s)s.textContent='';if(t){t.classList.remove('fading');t.textContent=EmptyHint}if(o)o.innerHTML='';if(i){i.removeAttribute('src');i.style.display='none'}}
function selectInteraction(qid,iid){if(!model||model.paused!==true)return;currentQuest=qid;currentInteraction=iid;send({command:'quest_select_interaction',questId:qid,id:iid});renderInteractions()}

function applyState(data){
    var app=$('questApp');if(!app)return;
    model=data;
    var paused=data.paused===true;
    /* Состояние приходит ~раз в секунду: логируем только ПЕРЕХОДЫ (пауза,
       выбранный интерактив, наличие диалога) — иначе поток одинаковых строк. */
    var stateKey=paused+'|'+(data.selectedInteraction||'')+'|'+(data.dialogue?'1':'0')+'|'+((data.nearby||[]).length);
    if(stateKey!==qdLastQuestStateKey){
        qdLastQuestStateKey=stateKey;
        qdLog('WS-IN(8085) quest_state paused='+paused+' selectedInteraction='+(data.selectedInteraction||'')+' nearby='+((data.nearby||[]).length)+' dialogue='+(data.dialogue?'yes':'no'));
    }
    app.classList.toggle('paused',paused);
    if(!paused){wasPaused=false;pagePaused=false;qdStateChanged();currentInteraction='';currentQuest='';lastDialogueKey='';clearDialogue();syncInput(false);return}
    wasPaused=true;pagePaused=true;qdStateChanged();
    /* Активный диалог приходит с выбранными идентификаторами — без них ответ
       игрока уходил бы без адреса. Если квест не пришёл, берём его из списка
       ближайших интерактивов. */
    if(data.selectedInteraction){
        currentInteraction=data.selectedInteraction;
        if(data.selectedQuest)currentQuest=data.selectedQuest;
        else if(!currentQuest){var m=(data.nearby||[]).find(function(p){return p.InteractionId===data.selectedInteraction});if(m)currentQuest=m.QuestId||''}
    }else if(data.selectedQuest)currentQuest=data.selectedQuest;
    syncInput(false);
    renderInteractions();renderQuests();renderInventory();
    if(data.dialogue){renderDialogue(data.dialogue)}
    else if(!data.selectedInteraction){currentInteraction='';currentQuest='';clearDialogue()}
    else if(currentInteraction){var keep=(data.nearby||[]).some(function(p){return p.QuestId===currentQuest&&p.InteractionId===currentInteraction&&p.Marker&&p.Marker!=='none'});if(!keep)clearDialogue()}
}
function connect(){try{ws=new WebSocket('ws://localhost:8085/');ws.onmessage=function(ev){try{var d=JSON.parse(ev.data);if(d.command==='quest_state')applyState(d);else if(d.command==='quest_error')showError(d.text)}catch(e){}};ws.onclose=function(){setTimeout(connect,1500)};ws.onerror=function(){try{ws.close()}catch(e){}}}catch(e){setTimeout(connect,1500)}}
function showError(text){var e=$('overlayError');if(!e)return;e.textContent=text||'Ошибка';e.classList.add('show');setTimeout(function(){e.classList.remove('show')},2500)}

/* Команды приложения, адресованные именно окну квестов. */
window.onEts2Command=function(d){
    if(!d)return;
    qdLog('WS-IN(8084) command='+d.command+' payload='+JSON.stringify(d));
    if(d.command==='set_quest_tab_state'){setTabPulse(d.hasInteractive);if(collapsed)syncInput(false)}
    else if(d.command==='set_quest_collapsed')applyCollapsed(d.collapsed,false,false);
    /* v1.0.40.59: TAB (хоткей приложения, активен только когда видна интерактивная
       категория) — ТО ЖЕ, что стрелочка сворачивания: развёрнуто → свернуть и
       оставить закладку, свёрнуто (видна закладка) → развернуть. */
    else if(d.command==='quest_toggle_collapse'){if(collapsed)expandWindow();else collapseWindow()}
};

(function(){
    /* Кнопка сворачивания, закладка и клик по прозрачной области окна. */
    var btn=$('collapseBtn'),tab=$('questTab'),app=$('questApp');
    if(btn)btn.addEventListener('click',function(e){e.stopPropagation();collapseWindow()});
    if(tab)tab.addEventListener('click',function(e){e.stopPropagation();expandWindow()});
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
    if(tab)tab.addEventListener('transitionend',function(){if(collapsed)syncInput(false)});
    window.addEventListener('resize',function(){if(collapsed)syncInput(false)});
    var style=document.createElement('style');
    style.textContent='#interactionList .sideItem{position:relative;padding-left:9px;padding-right:52px}#interactionList .sideMain{display:inline-block;vertical-align:middle;max-width:145px}.sideDist{position:absolute;right:9px;top:50%;transform:translateY(-50%);color:#768497;font-size:10px}.questSectionTitle{padding:8px 10px 5px;color:#ffd45a;font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.6px}.questItem em{display:block;margin-top:5px;color:#8c9aad;font-size:10px;font-style:normal;line-height:1.35}.questStepDetail{margin-top:12px;padding:10px;border-left:2px solid #ffd21f;background:rgba(255,210,31,.05);color:#b9c2ce}.questRewardTitle{margin-top:18px;margin-bottom:5px;color:#ffd45a;font-weight:700}.rewardLine{padding:3px 0;font-weight:600}.dialogOption{display:flex;flex-direction:column;gap:4px;align-items:flex-start}.optionReason{font-size:10px;color:#7e8a98;font-weight:400}.dialogTextRole{font-family:Roboto,"Roboto Regular","Segoe UI",Arial,sans-serif}.dialogTextService{font-family:"Courier New",Courier,monospace;color:rgba(255,255,255,.8);font-size:14px;margin-top:8px}.dialogTextService:before{content:""}.optionService{font-family:"Courier New",Courier,monospace;color:rgba(255,255,255,.8);font-size:12px}.optionRequirements{font-family:"Courier New",Courier,monospace;color:#0048ff;font-size:12px}.optionRequirements.unmet{color:#0048ff;opacity:.75}#dialogText.fading{opacity:0;transition:opacity 150ms ease}#dialogText{transition:opacity 150ms ease}.questWindow .panelTitle{font-size:13px}';
    document.head.appendChild(style);
})();

document.addEventListener('DOMContentLoaded',function(){
    clearDialogue();
    setTabPulse(false);
    /* Стартовое состояние всегда развёрнутое; фактический вид окна приходит
       от приложения командой set_quest_collapsed. */
    applyCollapsed(false,false,false);
    connect();
});
})();