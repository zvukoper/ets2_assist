(function(){
'use strict';

const PREFIX='quest-editor:';
let baseTargets=[];
let questPoints=[];
let questByKey=new Map();
let questSignature='';
let renderedQuestSignature='';
let selectedQuestKey='';
let applyingTargets=false;
let decorateQueued=false;
let saveBusy=false;

function $(id){return document.getElementById(id)}
function keyOf(p){return PREFIX+String(p.QuestId||p.questId||'')+':'+String(p.InteractionId||p.interactionId||'')}
function isQuestTarget(p){return String(p&&p.id||'').startsWith(PREFIX)||String(p&&p.GameName||'').startsWith(PREFIX)}
function questLabel(p){return String(p&&p.Name||p&&p.name||p&&p.InteractionId||'Квестовая точка')}
function markerLabel(marker){if(marker==='yellow_exclamation')return '!';if(marker==='yellow_question'||marker==='gray_question')return '?';return ''}
function markerClass(marker){return marker==='gray_question'?'gray':'yellow'}
function toTarget(p){
    const key=keyOf(p);
    return {id:key,uid:String(p.Uid||key),GameName:key,RealName:questLabel(p),name:questLabel(p),Category:'Квестовые',category:'__custom',Description:'Квестовая точка. Характеристики редактируются в редакторе квестов.',Enabled:true,ShowInAr:Boolean(p.ArVisible),ShowOnMap:Boolean(p.MinimapVisible),X:Number(p.X)||0,Y:Number(p.Y)||0,Z:Number(p.Z)||0,x:Number(p.X)||0,y:Number(p.Y)||0,z:Number(p.Z)||0,Color:'#ffd21f',color:'#ffd21f',Icon:'default',LabelStroke:1,TriggerRadius:Number(p.TriggerRadiusM)||35,CooldownMinutes:0,Hidden:0,DeleteOnComplete:0,DialogId:'',Action:'',Caption:'',EnterReward:0,AfterReward:0,EnterXp:0,AfterXp:0,source:'custom',__questPoint:true,__questData:p};
}
function questPointsAsTargets(){return questPoints.map(toTarget)}
function installCombinedTargets(){
    if(typeof window.MapEditor2SetTargets!=='function')return;
    applyingTargets=true;
    try{const regular=(Array.isArray(baseTargets)?baseTargets:[]).filter(p=>!isQuestTarget(p));window.MapEditor2SetTargets(regular.concat(questPointsAsTargets()))}
    finally{applyingTargets=false}
    queueDecorate();
}
function installSetTargetsWrapper(){
    if(typeof window.MapEditor2SetTargets!=='function'||window.__ets2QuestSetTargetsWrapped)return;
    const original=window.MapEditor2SetTargets;
    window.__ets2QuestSetTargetsWrapped=true;window.__ets2QuestOriginalSetTargets=original;
    window.MapEditor2SetTargets=function(items){
        baseTargets=(Array.isArray(items)?items:[]).filter(p=>!isQuestTarget(p));
        if(applyingTargets)return original(items);
        const combined=baseTargets.concat(questPointsAsTargets());
        applyingTargets=true;
        try{return original(combined)}finally{applyingTargets=false;queueDecorate()}
    };
}
function queueDecorate(){
    if(decorateQueued)return;
    decorateQueued=true;
    requestAnimationFrame(()=>{decorateQueued=false;decorateSidebar();syncQuestSelection();restrictQuestEditor()});
}
function findCustomCategory(){return document.querySelector('.category[data-cat-key="__custom"]')}
function findQuestButtons(){return Array.from(document.querySelectorAll('.pointButton[data-point-id]')).filter(b=>String(b.dataset.pointId||'').startsWith(PREFIX))}
function findQuestButton(key){return findQuestButtons().find(b=>b.dataset.pointId===key)||null}
function hideQuestSourceButtons(){
    const sourceUids=new Set();
    for(const p of questPoints){const uid=String(p&&p.OriginalUid||'').trim();if(uid)sourceUids.add(uid.toLowerCase())}
    if(!sourceUids.size)return;
    for(const b of document.querySelectorAll('.pointButton[data-point-id]')){
        const id=String(b.dataset.pointId||'');if(id.startsWith(PREFIX))continue;
        const lower=id.toLowerCase(),hide=[...sourceUids].some(uid=>lower.includes(':'+uid+':'));
        if(hide){b.style.display='none';b.dataset.questSourceHidden='1'}
        else if(b.dataset.questSourceHidden==='1'){b.style.display='';delete b.dataset.questSourceHidden}
    }
}
function decorateSidebar(){
    const list=$('categoryList');if(!list)return;
    const custom=findCustomCategory();
    if(custom){
        let regularCount=0;
        for(const b of custom.querySelectorAll('.pointButton')){const quest=String(b.dataset.pointId||'').startsWith(PREFIX);b.style.display=quest?'none':'';if(!quest&&!b.dataset.questSourceHidden)regularCount++}
        const count=custom.querySelector('.catCount');if(count)count.textContent=regularCount?String(regularCount):'';
        custom.style.display=regularCount?'':'none';
    }
    hideQuestSourceButtons();
    let cat=document.getElementById('questSidebarCategory'),created=false;
    if(!cat){
        cat=document.createElement('div');cat.id='questSidebarCategory';cat.className='category open';
        cat.innerHTML='<div class="categoryHead questCategoryHead"><span class="caret">▾</span><span class="catName">Квестовые</span><span class="catCount"></span></div><div class="categoryBody questCategoryBody"></div>';
        const first=list.firstElementChild;if(first)list.insertBefore(cat,first);else list.appendChild(cat);created=true;
    }
    const body=cat.querySelector('.questCategoryBody');if(!body)return;
    if(!created&&renderedQuestSignature===questSignature)return;
    body.innerHTML='';renderedQuestSignature=questSignature;
    if(!questPoints.length){body.innerHTML='<div class="empty">Квестовых точек нет</div>';const count=cat.querySelector('.catCount');if(count)count.textContent='';return}
    const frag=document.createDocumentFragment();
    for(const p of questPoints){
        const key=keyOf(p),b=document.createElement('button');b.type='button';b.className='pointButton questPointButton'+(selectedQuestKey===key?' selected':'');b.dataset.questKey=key;
        const marker=markerLabel(p.Marker),markerHtml=marker?`<span class="questMarker ${markerClass(p.Marker)}">${marker}</span>`:'';
        b.innerHTML=markerHtml+`<span class="pointName">${escapeHtml(questLabel(p))}</span>`;b.title=`${questLabel(p)} · ${p.IsGenerated?'сгенерированная точка':'квестовая точка'}`;b.addEventListener('click',()=>selectQuestPoint(key));frag.appendChild(b);
    }
    body.appendChild(frag);const count=cat.querySelector('.catCount');if(count)count.textContent=String(questPoints.length);
}
function escapeHtml(s){return String(s??'').replace(/[&<>\"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','\"':'&quot;',"'":'&#39;'}[c]))}
function selectQuestPoint(key){
    selectedQuestKey=key;queueDecorate();
    const custom=findCustomCategory();if(custom)custom.style.display='';
    const direct=findQuestButton(key);if(direct){direct.click();setTimeout(queueDecorate,50);return}
    const head=custom&&custom.querySelector('.categoryHead');if(head&&!custom.classList.contains('open'))head.click();
    let tries=0;const timer=setInterval(()=>{const button=findQuestButton(key);if(button){clearInterval(timer);button.click();setTimeout(queueDecorate,50);return}if(++tries>20){clearInterval(timer);queueDecorate()}},50);
}
function currentQuestButton(){return findQuestButtons().find(b=>b.classList.contains('selected'))||null}
function syncQuestSelection(){
    const button=currentQuestButton();if(button)selectedQuestKey=button.dataset.pointId||selectedQuestKey;
    const cat=$('questSidebarCategory');if(cat)cat.querySelectorAll('.questPointButton').forEach(b=>b.classList.toggle('selected',b.dataset.questKey===selectedQuestKey));
}
function fieldControl(key){return document.querySelector(`#rightBody [data-field-key="${CSS.escape(key)}"]`)||null}
function selectedQuestData(){
    if(!selectedQuestKey)return null;const raw=selectedQuestKey.slice(PREFIX.length),sep=raw.indexOf(':');if(sep<1)return null;return questByKey.get(raw)||null;
}
function questKeyParts(key){const raw=String(key||'').slice(PREFIX.length),i=raw.indexOf(':');return i<1?null:{questId:raw.slice(0,i),interactionId:raw.slice(i+1)}}
function restrictQuestEditor(){
    const p=selectedQuestData(),body=$('rightBody'),actions=$('editActionsBar');
    if(!p||!body||!actions)return;
    body.classList.add('questPointEditPanel');
    const allowed=new Set(['RealName','ShowInAr','ShowOnMap','X','Y','Z']);
    for(const row of body.querySelectorAll('.editRow[data-field-key]')){
        const key=row.dataset.fieldKey||'';row.style.display=allowed.has(key)?'':'none';
        const ctrl=row.querySelector('[data-field-key]');if(ctrl){ctrl.disabled=!allowed.has(key);if(key==='X'||key==='Y'||key==='Z'){ctrl.readOnly=true;ctrl.title='Координаты меняются перетаскиванием точки на карте';ctrl.tabIndex=-1}}
        const reset=row.querySelector('.resetBtn');if(reset)reset.style.display='none';
    }
    const fav=body.querySelector('.favStarRow');if(fav)fav.style.display='none';
    let meta=body.querySelector('.questPointEditorHint');if(!meta){meta=document.createElement('div');meta.className='editHint questPointEditorHint';body.insertBefore(meta,body.firstChild)}
    meta.innerHTML='<b>Квестовая точка</b><br>Можно перемещать точку на карте, менять название и видимость в AR/миникарте. Остальные параметры редактируются в редакторе квестов.';
    let save=actions.querySelector('.questPointSaveBtn'),cancel=actions.querySelector('.questPointCancelBtn');
    if(!save||!cancel){
        actions.innerHTML='';save=document.createElement('button');save.className='editBtn primary questPointSaveBtn';cancel=document.createElement('button');cancel.className='editBtn questPointCancelBtn';cancel.textContent='Отмена';
        actions.append(save,cancel);save.onclick=saveQuestPoint;cancel.onclick=cancelQuestPoint;
    }
    save.textContent=saveBusy?'Сохранение…':'Сохранить';save.disabled=saveBusy;cancel.disabled=saveBusy;
}
function readQuestForm(){
    const nameCtrl=fieldControl('RealName'),xCtrl=fieldControl('X'),yCtrl=fieldControl('Y'),zCtrl=fieldControl('Z'),arCtrl=fieldControl('ShowInAr'),mapCtrl=fieldControl('ShowOnMap');
    const x=Number(xCtrl&&xCtrl.value),y=Number(yCtrl&&yCtrl.value),z=Number(zCtrl&&zCtrl.value);if(!Number.isFinite(x)||!Number.isFinite(y)||!Number.isFinite(z))return null;
    const parts=questKeyParts(selectedQuestKey);if(!parts)return null;
    return {questId:parts.questId,interactionId:parts.interactionId,name:String(nameCtrl&&nameCtrl.value||'').trim(),x,y,z,arVisible:Boolean(arCtrl&&arCtrl.checked),minimapVisible:Boolean(mapCtrl&&mapCtrl.checked)};
}
function wsSend(payload){try{const ws=new WebSocket('ws://localhost:8085/');ws.onopen=()=>{try{ws.send(JSON.stringify(payload))}catch{}setTimeout(()=>{try{ws.close()}catch{}},300)};ws.onerror=()=>{try{ws.close()}catch{}}}catch{}}
function saveQuestPoint(){const data=readQuestForm();if(!data||!data.name){alert('Название квестовой точки не может быть пустым.');return}saveBusy=true;restrictQuestEditor();wsSend({command:'quest_editor_point_save',...data})}
function cancelQuestPoint(){const key=selectedQuestKey;saveBusy=false;installCombinedTargets();setTimeout(()=>{if(key&&questByKey.has(key.slice(PREFIX.length)))selectQuestPoint(key)},120)}
function applyQuestState(data){
    const points=Array.isArray(data&&data.editorPoints)?data.editorPoints:(Array.isArray(data&&data.points)?data.points:[]);
    const nextSignature=JSON.stringify(points.map(p=>({k:keyOf(p),u:p.Uid,n:p.Name,x:p.X,y:p.Y,z:p.Z,m:p.MinimapVisible,a:p.ArVisible,marker:p.Marker,origin:p.OriginalUid})));
    questByKey=new Map();for(const p of points){const k=keyOf(p);questByKey.set(k.slice(PREFIX.length),p)}questPoints=points;
    if(nextSignature!==questSignature){questSignature=nextSignature;installCombinedTargets()}else queueDecorate();
    saveBusy=false;queueDecorate();
}
window.MapEditor2InstallQuestPoints=function(base,points){
    installSetTargetsWrapper();baseTargets=Array.isArray(base)?base.filter(p=>!isQuestTarget(p)):[];questPoints=Array.isArray(points)?points:[];questByKey=new Map();questPoints.forEach(p=>{const k=keyOf(p);questByKey.set(k.slice(PREFIX.length),p)});
    questSignature=JSON.stringify(questPoints.map(p=>({k:keyOf(p),u:p.Uid,n:p.Name,x:p.X,y:p.Y,z:p.Z,m:p.MinimapVisible,a:p.ArVisible,marker:p.Marker,origin:p.OriginalUid})));renderedQuestSignature='';installCombinedTargets();
};
function connect(){try{const ws=new WebSocket('ws://localhost:8085/');ws.onmessage=function(ev){try{const d=JSON.parse(ev.data);if(d&&d.command==='quest_state')applyQuestState(d)}catch{}};ws.onclose=()=>setTimeout(connect,1500);ws.onerror=()=>{try{ws.close()}catch{}}}catch{setTimeout(connect,1500)}}
const style=document.createElement('style');style.textContent=`
#questSidebarCategory .categoryHead{background:rgba(255,210,31,.20)!important;color:#ffd21f!important}
#questSidebarCategory .questCategoryBody{display:block!important}
#questSidebarCategory .questPointButton{display:flex!important}
#questSidebarCategory .questMarker{width:15px;display:inline-flex;align-items:center;justify-content:center;font-weight:800;font-size:14px;line-height:1}
#questSidebarCategory .questMarker.yellow{color:#ffd21f}
#questSidebarCategory .questMarker.gray{color:#9da5af}
#questSidebarCategory .questPointButton .pointName{font-weight:600}
#questSidebarCategory .questPointButton.selected{background:#394454!important}
.questPointEditPanel .editMeta{color:#7f8a98}
.questPointEditPanel .questPointEditorHint{border:1px solid rgba(255,210,31,.28);background:rgba(255,210,31,.06);margin:0 0 8px}
`;
document.head.appendChild(style);
const observer=new MutationObserver(()=>queueDecorate());observer.observe(document.body,{childList:true,subtree:true});
installSetTargetsWrapper();window.chrome?.webview?.postMessage('map2-quest-bridge-ready');connect();queueDecorate();
})();
