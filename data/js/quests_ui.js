(function(){
'use strict';
var ws=null,model=null,currentQuest='',currentInteraction='',wasPaused=false,lastLoadingNonce=-1,loadingTimer=null;
var $=function(id){return document.getElementById(id)};
function esc(s){return String(s==null?'':s).replace(/[&<>\"']/g,function(c){return({'&':'&amp;','<':'&lt;','>':'&gt;','\"':'&quot;',"'":'&#39;'})[c]})}
function send(o){if(ws&&ws.readyState===WebSocket.OPEN)ws.send(JSON.stringify(o))}
function setNativeClickable(value){try{if(window.chrome&&window.chrome.webview)window.chrome.webview.postMessage(JSON.stringify({command:'set_clickable',value:!!value}));}catch(e){}}
function markerIcon(m){if(m==='yellow_exclamation')return'editor_static_data/icons/quest_exclamation_yellow.svg';if(m==='yellow_question')return'editor_static_data/icons/quest_question_yellow.svg';if(m==='gray_question')return'editor_static_data/icons/quest_question_gray.svg';return''}
function showLoading(data){
    var old=$('questLoading');if(old)old.remove();if(loadingTimer)clearTimeout(loadingTimer);
    var l=(data&&data.loadingScreen)||{};var dur=Math.max(900,Number(l.durationMs)||2200);
    var box=document.createElement('div');box.id='questLoading';
    box.innerHTML='<div class="questLoadingInner"><img class="questLoadingLogo" src="ets2a_logo.png"><div class="questLoadingImageWrap"><img class="questLoadingImage"></div><div class="questLoadingBar"><i></i></div><div class="questLoadingText">Загрузка интерактивов…</div></div>';
    var st=document.createElement('style');st.id='questLoadingStyle';st.textContent='#questLoading{position:fixed;inset:0;z-index:500;display:flex;align-items:center;justify-content:center;background:rgba(5,8,12,.985);opacity:0;pointer-events:auto;transition:opacity .45s ease;font-family:Segoe UI,Arial,sans-serif}#questLoading .questLoadingInner{width:min(680px,78vw);display:flex;flex-direction:column;align-items:center;gap:16px;transform:translateY(8px) scale(.98);transition:transform .7s ease}#questLoading .questLoadingLogo{max-width:320px;max-height:110px;filter:drop-shadow(0 4px 20px rgba(0,0,0,.75));opacity:.95}#questLoading .questLoadingImageWrap{width:min(620px,74vw);height:min(300px,32vh);display:flex;align-items:center;justify-content:center;overflow:hidden;border:1px solid rgba(255,210,31,.18);background:rgba(255,255,255,.025);border-radius:8px;box-shadow:0 12px 45px rgba(0,0,0,.5)}#questLoading .questLoadingImage{max-width:100%;max-height:100%;width:100%;height:100%;object-fit:cover;opacity:.88;transform:scale(1.02);transition:transform '+dur+'ms ease,opacity .6s ease}#questLoading .questLoadingBar{width:min(420px,60vw);height:5px;border-radius:6px;background:#202934;overflow:hidden;box-shadow:inset 0 0 0 1px rgba(255,255,255,.03)}#questLoading .questLoadingBar i{display:block;width:28%;height:100%;background:#ffd21f;border-radius:6px;transform:translateX(-160%);transition:transform '+Math.max(700,dur-300)+'ms ease}#questLoading .questLoadingText{color:#aeb8c5;font:600 13px/1.2 Segoe UI,Arial,sans-serif;opacity:0;transition:opacity .4s ease}';document.head.appendChild(st);document.body.appendChild(box);
    var image=box.querySelector('.questLoadingImage');if(image){image.src=l.image||'images/quest/ruslan.svg';}
    requestAnimationFrame(function(){box.style.opacity='1';box.querySelector('.questLoadingInner').style.transform='translateY(0) scale(1)';box.querySelector('.questLoadingText').style.opacity='1';box.querySelector('.questLoadingBar i').style.transform='translateX(350%)';});
    loadingTimer=setTimeout(function(){box.style.opacity='0';box.querySelector('.questLoadingImage').style.transform='scale(1.08)';setTimeout(function(){box.remove();var css=$('questLoadingStyle');if(css)css.remove()},480)},dur);
}
function renderInteractions(){
    var el=$('interactionList');if(!el||!model)return;
    var near=(model.nearby||[]).filter(function(p){return p.Marker&&p.Marker!=='none'});
    if(!near.length){el.innerHTML='<div class="muted">Нет доступных интерактивов</div>';return}
    el.innerHTML=near.map(function(p){var active=p.QuestId===currentQuest&&p.InteractionId===currentInteraction;var icon=markerIcon(p.Marker);return'<button class="sideItem'+(active?' selected':'')+'" data-q="'+esc(p.QuestId)+'" data-i="'+esc(p.InteractionId)+'"><img src="'+icon+'"><span class="sideMain">'+esc(p.Name)+'</span><span class="sideDist">'+Math.round(p.distance||0)+' м</span></button>'}).join('');
    el.querySelectorAll('.sideItem').forEach(function(b){b.onclick=function(){selectInteraction(b.dataset.q,b.dataset.i)}})
}
function renderQuests(){
    var el=$('questList');if(!el||!model)return;
    var active=model.activeQuests||[],archive=model.archiveQuests||[];
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
    if(text)text.innerHTML=esc(q.description||'')+(q.stepDescription?'<div class="questStepDetail">'+esc(q.stepDescription)+'</div>':'')+'<div class="questRewardTitle">Награды</div>'+((q.rewards||[]).map(function(r){var col=r.color?' style="color:'+esc(r.color)+'"':'';return'<div class="rewardLine"'+col+'>'+esc(r.display||r.id)+' x'+esc(r.amount||1)+'</div>'}).join('')||'<div class="muted">—</div>');
    if(opts)opts.innerHTML='';if(img){img.removeAttribute('src');img.style.display='none'}
    renderInteractions();
}
function renderInventory(){
    var el=$('inventory');if(!el||!model)return;var items=model.inventory||[];
    el.innerHTML='<span class="inventoryTitle">Инвентарь</span> '+(items.length?items.map(function(x){return'<span class="inventoryItem">'+esc(x.name||x.id)+' ×'+esc(x.amount||0)+'</span>'}).join(' '):'<span class="muted">пусто</span>');
}
function renderDialogue(node){
    var speaker=$('dialogSpeaker'),text=$('dialogText'),img=$('dialogImage'),opts=$('dialogOptions');
    if(speaker)speaker.textContent=node.speaker||'';if(text)text.textContent=node.text||'';
    if(img){img.src=node.image||'';img.style.display=node.image?'block':'none'}
    if(opts){opts.innerHTML=(node.options||[]).map(function(o,i){return'<button class="dialogOption" data-index="'+i+'" '+(o.enabled===false?'disabled':'')+'>'+esc(o.text)+(o.enabled===false?'<small class="optionReason">'+esc(o.reason||'Требование не выполнено')+'</small>':'')+'</button>'}).join('');opts.querySelectorAll('.dialogOption').forEach(function(b){b.onclick=function(){send({command:'quest_dialog_option',questId:currentQuest,interaction:currentInteraction,index:Number(b.dataset.index)})}})}
}
function clearDialogue(){var s=$('dialogSpeaker'),t=$('dialogText'),o=$('dialogOptions'),i=$('dialogImage');if(s)s.textContent='';if(t)t.textContent='Выберите интерактив слева.';if(o)o.innerHTML='';if(i){i.removeAttribute('src');i.style.display='none'}}
function selectInteraction(qid,iid){if(!model||model.paused!==true)return;currentQuest=qid;currentInteraction=iid;send({command:'quest_select_interaction',questId:qid,id:iid});renderInteractions()}
function applyState(data){
    var app=$('questApp');if(!app)return;
    var paused=data.paused===true;
    setNativeClickable(paused);
    app.classList.toggle('paused',paused);
    if(paused&&!wasPaused&&data.loadingNonce!==lastLoadingNonce){lastLoadingNonce=Number(data.loadingNonce||0);showLoading(data)}
    if(paused&&data.loadingNonce!==lastLoadingNonce){lastLoadingNonce=Number(data.loadingNonce||0)}
    if(!paused){wasPaused=false;currentInteraction='';return}
    wasPaused=true;model=data;
    renderInteractions();renderQuests();renderInventory();
    if(data.dialogue){renderDialogue(data.dialogue)}
    else if(currentInteraction){var keep=(data.nearby||[]).some(function(p){return p.QuestId===currentQuest&&p.InteractionId===currentInteraction&&p.Marker&&p.Marker!=='none'});if(!keep)clearDialogue()}
    if(!currentInteraction){var p=(data.nearby||[]).find(function(x){return x.Marker&&x.Marker!=='none'});if(p)selectInteraction(p.QuestId,p.InteractionId)}
    if(!(data.nearby||[]).some(function(x){return x.Marker&&x.Marker!=='none'})&& !data.dialogue)clearDialogue();
}
function connect(){try{ws=new WebSocket('ws://localhost:8085/');ws.onopen=function(){send({command:'quest_ping'})};ws.onmessage=function(ev){try{var d=JSON.parse(ev.data);if(d.command==='quest_state')applyState(d);else if(d.command==='quest_error')showError(d.text)}catch(e){}};ws.onclose=function(){setTimeout(connect,1500)};ws.onerror=function(){try{ws.close()}catch(e){}}}catch(e){setTimeout(connect,1500)}}
function showError(text){var e=$('overlayError');if(!e)return;e.textContent=text||'Ошибка';e.classList.add('show');setTimeout(function(){e.classList.remove('show')},2500)}
(function(){var style=document.createElement('style');style.textContent='#interactionList .sideItem{position:relative;padding-left:9px;padding-right:52px}#interactionList .sideMain{display:inline-block;vertical-align:middle;max-width:145px}.sideDist{position:absolute;right:9px;top:50%;transform:translateY(-50%);color:#768497;font-size:10px}.questSectionTitle{padding:8px 10px 5px;color:#ffd45a;font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.6px}.questItem em{display:block;margin-top:5px;color:#8c9aad;font-size:10px;font-style:normal;line-height:1.35}.questStepDetail{margin-top:12px;padding:10px;border-left:2px solid #ffd21f;background:rgba(255,210,31,.05);color:#b9c2ce}.questRewardTitle{margin-top:18px;margin-bottom:5px;color:#ffd45a;font-weight:700}.rewardLine{padding:3px 0;font-weight:600}.dialogOption{display:flex;flex-direction:column;gap:4px}.optionReason{font-size:10px;color:#7e8a98;font-weight:400}.questWindow .panelTitle{font-size:13px}.questWindow{grid-template-columns:240px minmax(420px,1fr) 310px}@media(max-width:1100px){.questWindow{grid-template-columns:190px minmax(0,1fr) 240px}.interactionList .sideMain{max-width:110px}}';document.head.appendChild(style)})();
document.addEventListener('DOMContentLoaded',function(){clearDialogue();setNativeClickable(false);connect();});
})();