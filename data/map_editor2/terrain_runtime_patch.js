const terrainCanvas=document.getElementById('terrain'),terrainCtx=terrainCanvas?.getContext('2d');
let terrainImage=null,terrainMeta=null;
async function loadTerrain(){try{const m=await fetch('../map_editor2/terrain_height_meta.json?ts='+Date.now(),{cache:'no-store'});if(!m.ok)throw Error('HTTP '+m.status);terrainMeta=await m.json();const img=new Image();img.src='../map_editor2/terrain_height.png?ts='+Date.now();await img.decode();terrainImage=img;}catch(e){terrainMeta=null;terrainImage=null;console.warn('Terrain load failed',e)}dirty=true;requestFrame()}
function drawTerrain(){if(!terrainCanvas||!terrainCtx||!terrainImage||!terrainMeta)return;const s=mapSize();terrainCanvas.width=Math.floor(s.w*dpr);terrainCanvas.height=Math.floor(s.h*dpr);terrainCanvas.style.width=s.w+'px';terrainCanvas.style.height=s.h+'px';terrainCtx.setTransform(dpr,0,0,dpr,0,0);terrainCtx.clearRect(0,0,s.w,s.h);const mx0=Number(terrainMeta.minX),mx1=Number(terrainMeta.maxX),mz0=Number(terrainMeta.minZ),mz1=Number(terrainMeta.maxZ);const sx=s.w*.5+(mx0-camera.x)/camera.mpp,sy=s.h*.5+(mz0-camera.z)/camera.mpp,ex=s.w*.5+(mx1-camera.x)/camera.mpp,ey=s.h*.5+(mz1-camera.z)/camera.mpp;terrainCtx.drawImage(terrainImage,sx,sy,ex-sx,ey-sy)}
function terrainProgress(text){status.textContent='Map Editor 2 · '+text}
window.MapEditor2TerrainProgress=terrainProgress;window.MapEditor2ReloadTerrain=loadTerrain;
window.addEventListener('resize',()=>{if(!terrainCanvas)return;const s=mapSize();terrainCanvas.width=Math.floor(s.w*dpr);terrainCanvas.height=Math.floor(s.h*dpr);terrainCanvas.style.width=s.w+'px';terrainCanvas.style.height=s.h+'px';dirty=true;requestFrame()});

(function applyEditorTypography(){if(document.getElementById('map2-muted-typography'))return;const style=document.createElement('style');style.id='map2-muted-typography';style.textContent=`
#topMenu .brand,#topMenu .menuItem,#pointTools .tool,#pointTools .tool.active,#categoryList .categoryHead,#categoryList .catCount,#categoryList .caret,#categoryList .eye,#categoryList .pointButton,#categoryList .pointName,#status,#mapInfo,#rightTitle,#rightBody .hint,#rightBody .label,#rightBody .value,#footer,#footer span{color:#b8c0cb!important}
#categoryList .pointButton.selected .pointName{color:#ffffff!important}
#rightBody .value{color:#c0c7d0!important}
#topMenu .menuItem:hover,#categoryList .categoryHead:hover{color:#c4ccd6!important}
#topMenu .menuItem[data-menu="tools"]{color:#b8c0cb!important}
.menuPopup .menuBtn{color:#b8c0cb!important}
`;document.head.appendChild(style)})();

function selectedPointFor(p){return selectedIds.has(p.id)||!!(selectedPoint&&selectedPoint.id===p.id)}
function visibleCityPoints(){return categories.find(c=>c.key==='__cities')?.points||[]}
function drawPointFill(p,q,radius){const c=parseColor(p.color);labelCtx.beginPath();labelCtx.arc(q.x,q.y,radius,0,Math.PI*2);labelCtx.fillStyle=`rgb(${Math.round(c[0]*255)},${Math.round(c[1]*255)},${Math.round(c[2]*255)})`;labelCtx.fill();labelCtx.lineWidth=2;labelCtx.strokeStyle='#0a0c0f';labelCtx.stroke()}
function drawSelectionRing(q,radius){labelCtx.beginPath();labelCtx.arc(q.x,q.y,radius+3.5,0,Math.PI*2);labelCtx.lineWidth=3;labelCtx.strokeStyle='lime';labelCtx.stroke()}
function drawPointStyle(p){const q=worldToScreen(p),selected=selectedPointFor(p),isCity=p.isCity===true,radius=isCity?7:6;if(selected)drawSelectionRing(q,radius);drawPointFill(p,q,radius);return{q,radius,selected,isCity}}
function drawPointLabel(p,info){const q=info.q,selected=info.selected,isCity=info.isCity,radius=info.radius;const size=10*fontScale*(isCity?1.04:1)*(selected?1.08:1);labelCtx.font=`${selected?'700':'600'} ${size.toFixed(2)}px Segoe UI,Arial,sans-serif`;labelCtx.textAlign='center';labelCtx.textBaseline='alphabetic';labelCtx.fillStyle=selected?'#ffffff':'#b8c0cb';labelCtx.lineWidth=4;labelCtx.strokeStyle='rgba(0,0,0,.92)';const labelY=q.y-radius-6;const centerX=q.x;labelCtx.strokeText(String(p.name??''),centerX,labelY);labelCtx.fillText(String(p.name??''),centerX,labelY)}

drawPoint=function(p){return drawPointStyle(p)};
drawPointsAndLabels=function(){const s=mapSize();labelCtx.setTransform(dpr,0,0,dpr,0,0);labelCtx.clearRect(0,0,s.w,s.h);updateSelectionUi();if(camera.mpp>800)return;
const visible=getVisiblePoints();
const cityMap=new Map(visibleCityPoints().map(p=>[p.id,p]));
for(const p of visible.filter(p=>p.isCity))cityMap.set(p.id,p);
const cities=Array.from(cityMap.values());
const cityIds=new Set(cities.map(p=>p.id));
const normal=visible.filter(p=>!p.isCity&&!cityIds.has(p.id));
const normalPlain=[],normalSelected=[],cityPlain=[],citySelected=[];
for(const p of normal)(selectedPointFor(p)?normalSelected:normalPlain).push(p);
for(const p of cities)(selectedPointFor(p)?citySelected:cityPlain).push(p);
const drawIfVisible=(p,radius)=>{const q=worldToScreen(p);if(q.x<-140||q.x>s.w+140||q.y<-90||q.y>s.h+70)return null;drawPointStyle(p);return{q,radius,selected:selectedPointFor(p),isCity:p.isCity===true}};
for(const p of normalPlain)drawIfVisible(p,6);
for(const p of cityPlain)drawIfVisible(p,7);
for(const p of normalSelected)drawIfVisible(p,6);
for(const p of citySelected)drawIfVisible(p,7);
for(const p of normalPlain){const q=worldToScreen(p);if(q.x<-140||q.x>s.w+140||q.y<-90||q.y>s.h+70)continue;drawPointLabel(p,{q,radius:6,selected:false,isCity:false})}
for(const p of cityPlain){const q=worldToScreen(p);if(q.x<-140||q.x>s.w+140||q.y<-90||q.y>s.h+70)continue;drawPointLabel(p,{q,radius:7,selected:false,isCity:true})}
for(const p of normalSelected){const q=worldToScreen(p);if(q.x<-140||q.x>s.w+140||q.y<-90||q.y>s.h+70)continue;drawPointLabel(p,{q,radius:6,selected:true,isCity:false})}
for(const p of citySelected){const q=worldToScreen(p);if(q.x<-140||q.x>s.w+140||q.y<-90||q.y>s.h+70)continue;drawPointLabel(p,{q,radius:7,selected:true,isCity:true})}
}

function updateSelectionUi(){const selectedTotal=selectedIds.size+(selectedPoint&&!multiMode&&!selectedIds.has(selectedPoint.id)?1:0);let counter=document.getElementById('map2-selected-count');if(!counter){counter=document.createElement('span');counter.id='map2-selected-count';counter.style.marginLeft='2px';document.querySelector('.onlySelected')?.appendChild(counter)}counter.textContent=selectedTotal>0?`(${selectedTotal.toLocaleString()})`:'';document.querySelectorAll('.category').forEach((el,i)=>{const cat=categories[i];if(!cat)return;const count=cat.points.reduce((n,p)=>n+(selectedIds.has(p.id)||(selectedPoint&&!multiMode&&selectedPoint.id===p.id)?1:0),0);const node=el.querySelector('.catCount');if(!node)return;if(multiMode)node.textContent=count>0?count.toLocaleString():'';else node.textContent=cat.points.length.toLocaleString()})}

(function ensureToolsMenu(){let item=document.querySelector('[data-menu="tools"]');let popup=document.getElementById('toolsPopup');if(!item){item=document.createElement('div');item.className='menuItem';item.dataset.menu='tools';item.textContent='Инструменты';const service=document.querySelector('[data-menu="service"]');if(service?.parentNode)service.parentNode.insertBefore(item,service.nextSibling);else document.getElementById('topMenu')?.appendChild(item)}if(!popup){popup=document.createElement('div');popup.id='toolsPopup';popup.className='menuPopup';popup.innerHTML='<button class="menuBtn" id="generateTerrain">Генерировать карту высот</button>';document.getElementById('topMenu')?.appendChild(popup)}popup.style.left=Math.max(0,(item.offsetLeft||0)-4)+'px';if(!item.dataset.map2ToolsBound){item.dataset.map2ToolsBound='1';item.addEventListener('click',e=>{e.stopPropagation();popup.style.left=Math.max(0,(item.offsetLeft||0)-4)+'px';popup.classList.toggle('open');if(typeof viewPopup!=='undefined')viewPopup.classList.remove('open')})}const gen=document.getElementById('generateTerrain');if(gen&&!gen.dataset.map2TerrainBound){gen.dataset.map2TerrainBound='1';gen.addEventListener('click',e=>{e.stopPropagation();popup.classList.remove('open');window.chrome?.webview?.postMessage('map2-generate-terrain')})}})();

document.addEventListener('click',e=>{const target=e.target;if(target instanceof Element&&!target.closest('.menuPopup')&&!target.closest('.menuItem'))document.querySelectorAll('.menuPopup.open').forEach(x=>x.classList.remove('open'))});

const originalFindPointAt=findPointAt;findPointAt=function(px,py){const candidates=[];for(const p of getVisiblePoints()){const q=worldToScreen(p),d=Math.hypot(q.x-px,q.y-py);const hit=p.isCity?14:13;if(d<=hit)candidates.push({p,d,selected:selectedPointFor(p)})}for(const p of visibleCityPoints()){const q=worldToScreen(p),d=Math.hypot(q.x-px,q.y-py);if(d<=14&&!candidates.some(x=>x.p.id===p.id))candidates.push({p,d,selected:selectedPointFor(p)})}candidates.sort((a,b)=>(Number(b.selected)-Number(a.selected))||Number(a.d-b.d));return candidates[0]?.p||null};

const originalRender=render;render=function(){if(!dirty)return;dirty=false;gl.viewport(0,0,glCanvas.width,glCanvas.height);gl.clearColor(15/255,18/255,23/255,1);gl.clear(gl.COLOR_BUFFER_BIT);drawTerrain();updateGrid();drawRoads();drawPointsAndLabels();mapInfo.textContent=`Zoom ${camera.mpp.toFixed(3)} m/px · клетка ${gridWorldStep.toFixed(3)} м · roads ${roadsCount.toLocaleString()} · points ${allPoints.length.toLocaleString()} · cities ${visibleCityPoints().length.toLocaleString()}`};
loadTerrain();
