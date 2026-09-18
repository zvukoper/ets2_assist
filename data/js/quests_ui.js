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
function send(o){if(ws&&ws.readyState===WebSocket.OPEN)ws.send(JSON.stringify(o))}
function post(o){try{if(window.chrome&&window.chrome.webview)window.chrome.webview.postMessage(JSON.stringify(o));}catch(e){}}
function setNativeClickable(value){post({command:'set_clickable',value:!!value})}
function markerIcon(m){if(m==='yellow_exclamation')return'editor_static_data/icons/quest_exclamation_yellow.svg';if(m==='yellow_question')return'editor_static_data/icons/quest_question_yellow.svg';if(m==='gray_question')return'editor_static_data/icons/quest_question_gray.svg';return''}

/* ---------------------------------------------------------------- сворачивание */
/* Мышь окна управляется из двух состояний: активна ли пауза и свёрнуто ли окно.
   Развёрнутое окно кликабельно целиком; свёрнутое отдаёт мыши только область
   закладки у левой границы экрана; скрытое окно прозрачно для мыши. */
function syncInput(notify){
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
            post({command:'set_clickable_hotspot',
                xr:0,
                yr:Math.max(0,(r.top-pad)/vh),
                wr:Math.min(1,(r.width+8)/vw),
                hr:Math.min(1,(r.height+pad*2)/vh)});
        }else post({command:'set_clickable_hotspot',xr:0,yr:0,wr:0,hr:0});
    }else{
        setNativeClickable(false);
        post({command:'set_clickable_hotspot',xr:0,yr:0,wr:0,hr:0});
    }
    if(notify)post({command:'return_focus'});
}
function applyCollapsed(value,notify,report){
    collapsed=!!value;
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
    el.innerHTML=near.map(function(p){var active=p.QuestId===currentQuest&&p.InteractionId===currentInteraction;var icon=markerIcon(p.Marker);return'<button class="sideItem'+(active?' selected':'')+'" data-q="'+esc(p.QuestId)+'" data-i="'+esc(p.InteractionId)+'"><img src="'+icon+'"><span class="sideMain">'+esc(p.Name)+'</span><span class="sideDist">'+Math.round(p.distance||0)+' м</span></button>'}).join('');
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
    app.classList.toggle('paused',paused);
    if(!paused){wasPaused=false;pagePaused=false;currentInteraction='';currentQuest='';lastDialogueKey='';clearDialogue();syncInput(false);return}
    wasPaused=true;pagePaused=true;
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
    if(d.command==='set_quest_tab_state'){setTabPulse(d.hasInteractive);if(collapsed)syncInput(false)}
    else if(d.command==='set_quest_collapsed')applyCollapsed(d.collapsed,false,false);
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