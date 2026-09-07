const terrainCanvas=document.getElementById('terrain'),terrainCtx=terrainCanvas?.getContext('2d');
let terrainImage=null,terrainMeta=null,terrainVisible=true;
const terrainAssetLabel='data\\map_editor2\\terrain_height.png';
async function loadTerrain(){
  try{
    const metaResponse=await fetch('../map_editor2/terrain_height_meta.json?ts='+Date.now(),{cache:'no-store'});
    if(!metaResponse.ok)throw Error('META HTTP '+metaResponse.status);
    terrainMeta=await metaResponse.json();
    const image=new Image();
    image.src='../map_editor2/terrain_height.png?ts='+Date.now();
    await image.decode();
    terrainImage=image;
    terrainProgress('Карта высот загружена · '+terrainAssetLabel);
  }catch(e){
    terrainMeta=null;
    terrainImage=null;
    terrainProgress('Карта высот не найдена · сначала сгенерируйте её');
    console.warn('Terrain load failed',e);
  }
  dirty=true;requestFrame();
}
function drawTerrain(){
  if(!terrainCanvas||!terrainCtx)return;
  const s=mapSize();
  terrainCanvas.width=Math.floor(s.w*dpr);
  terrainCanvas.height=Math.floor(s.h*dpr);
  terrainCanvas.style.width=s.w+'px';
  terrainCanvas.style.height=s.h+'px';
  terrainCtx.setTransform(dpr,0,0,dpr,0,0);
  terrainCtx.clearRect(0,0,s.w,s.h);
  if(!terrainVisible||!terrainImage||!terrainMeta)return;
  const mx0=Number(terrainMeta.minX),mx1=Number(terrainMeta.maxX),mz0=Number(terrainMeta.minZ),mz1=Number(terrainMeta.maxZ);
  if(![mx0,mx1,mz0,mz1].every(Number.isFinite)||mx1<=mx0||mz1<=mz0)return;
  const sx=s.w*.5+(mx0-camera.x)/camera.mpp;
  const sy=s.h*.5+(mz0-camera.z)/camera.mpp;
  const ex=s.w*.5+(mx1-camera.x)/camera.mpp;
  const ey=s.h*.5+(mz1-camera.z)/camera.mpp;
  terrainCtx.save();
  terrainCtx.globalAlpha=.78;
  terrainCtx.filter='brightness(1.28) saturate(.9)';
  terrainCtx.imageSmoothingEnabled=true;
  terrainCtx.drawImage(terrainImage,sx,sy,ex-sx,ey-sy);
  terrainCtx.restore();
}
function terrainProgress(text){status.textContent='Map Editor 2 · '+text}
window.MapEditor2TerrainProgress=terrainProgress;
window.MapEditor2ReloadTerrain=loadTerrain;
window.MapEditor2ToggleTerrain=()=>{terrainVisible=!terrainVisible;updateTerrainButton();terrainProgress(terrainVisible?'Карта высот включена · '+terrainAssetLabel:'Карта высот скрыта · '+terrainAssetLabel);dirty=true;requestFrame()};
window.addEventListener('resize',()=>{if(!terrainCanvas)return;dirty=true;requestFrame()});

(function applyEditorTypography(){
  if(document.getElementById('map2-muted-typography'))return;
  const style=document.createElement('style');
  style.id='map2-muted-typography';
  style.textContent=`
#topMenu .brand,#topMenu .menuItem,#pointTools .tool,#pointTools .tool.active,#categoryList .categoryHead,#categoryList .catCount,#categoryList .caret,#categoryList .eye,#categoryList .pointButton,#categoryList .pointName,#status,#mapInfo,#rightTitle,#rightBody .hint,#rightBody .label,#rightBody .value,#footer,#footer span{color:#b8c0cb!important}
#categoryList .pointButton.selected .pointName{color:#ffffff!important}
#topMenu .menuItem:hover,#categoryList .categoryHead:hover{color:#c4ccd6!important}
.menuPopup .menuBtn{color:#b8c0cb!important}
#map2-terrain-toggle{color:#b8c0cb!important}
`;
  document.head.appendChild(style);
})();

function selectedPointFor(p){return selectedIds.has(p.id)||!!(selectedPoint&&selectedPoint.id===p.id)}
function visibleCityPoints(){return categories.find(c=>c.key==='__cities')?.points||[]}
function drawSelectionRing(q,radius){
  labelCtx.save();
  labelCtx.beginPath();
  labelCtx.arc(q.x,q.y,radius+4,0,Math.PI*2);
  labelCtx.lineWidth=3;
  labelCtx.strokeStyle='lime';
  labelCtx.stroke();
  labelCtx.restore();
}
function drawPointFill(p,q,radius){
  const selected=selectedPointFor(p);
  const c=parseColor(p.color);
  if(selected)drawSelectionRing(q,radius);
  labelCtx.save();
  labelCtx.beginPath();
  labelCtx.arc(q.x,q.y,radius,0,Math.PI*2);
  labelCtx.fillStyle=`rgb(${Math.round(c[0]*255)},${Math.round(c[1]*255)},${Math.round(c[2]*255)})`;
  labelCtx.fill();
  labelCtx.lineWidth=1;
  labelCtx.strokeStyle='#0a0c0f';
  labelCtx.stroke();
  labelCtx.restore();
}
function drawPointLabel(p,q,radius,{city=false,selected=false}={}){
  const cityFactor=city?1.16:1;
  const size=10*fontScale*cityFactor*(selected&&!city?1.05:1);
  labelCtx.save();
  labelCtx.font=`${city||selected?'700':'600'} ${size.toFixed(2)}px Segoe UI,Arial,sans-serif`;
  labelCtx.textAlign='center';
  labelCtx.textBaseline='alphabetic';
  labelCtx.fillStyle=city?'#ffe600':(selected?'#ffffff':'#b8c0cb');
  labelCtx.lineWidth=1;
  labelCtx.strokeStyle='rgba(0,0,0,.82)';
  labelCtx.shadowColor='rgba(0,0,0,.70)';
  labelCtx.shadowBlur=2;
  labelCtx.shadowOffsetX=0;
  labelCtx.shadowOffsetY=1;
  const labelY=q.y-radius-5;
  labelCtx.strokeText(p.name,q.x,labelY);
  labelCtx.fillText(p.name,q.x,labelY);
  labelCtx.restore();
}
function drawNormalPoint(p){
  const q=worldToScreen(p);if(q.x<-100||q.x>mapSize().w+100||q.y<-60||q.y>mapSize().h+40)return;
  const selected=selectedPointFor(p),radius=selected?8:6;
  drawPointFill(p,q,radius);
}
function drawCityPoint(p){
  const q=worldToScreen(p);if(q.x<-130||q.x>mapSize().w+130||q.y<-80||q.y>mapSize().h+50)return;
  const selected=selectedPointFor(p),radius=selected?9:7;
  drawPointFill(p,q,radius);
}
drawPoint=(p)=>drawNormalPoint(p);

drawPointsAndLabels=function(){
  const s=mapSize();
  labelCtx.setTransform(dpr,0,0,dpr,0,0);
  labelCtx.clearRect(0,0,s.w,s.h);
  if(camera.mpp>800)return;

  const visible=getVisiblePoints();
  const normal=visible.filter(p=>!p.isCity);
  const cities=visibleCityPoints().filter(p=>categoryVisible(p.category));
  const normalSelected=normal.filter(selectedPointFor);
  const normalPlain=normal.filter(p=>!selectedPointFor(p));

  // 1. All ordinary point bodies.
  let drawn=0;
  for(const p of normalPlain){if(++drawn>1800)break;drawNormalPoint(p)}
  // 2. Selected ordinary points are always above ordinary points.
  for(const p of normalSelected)drawNormalPoint(p);
  // 3. Ordinary labels are above all ordinary point bodies.
  for(const p of normalPlain){const q=worldToScreen(p);if(q.x<-100||q.x>s.w+100||q.y<-60||q.y>s.h+40)continue;drawPointLabel(p,q,6,{city:false,selected:false})}
  for(const p of normalSelected){const q=worldToScreen(p);if(q.x<-100||q.x>s.w+100||q.y<-60||q.y>s.h+40)continue;drawPointLabel(p,q,8,{city:false,selected:true})}
  // 4. City bodies are above every ordinary object.
  for(const p of cities)drawCityPoint(p);
  // 5. All city names are the final map layer: no city point can cover another city name.
  for(const p of cities){const q=worldToScreen(p);if(q.x<-130||q.x>s.w+130||q.y<-80||q.y>s.h+50)continue;drawPointLabel(p,q,selectedPointFor(p)?9:7,{city:true,selected:selectedPointFor(p)})}
}

function updateSelectionUi(){
  const total=selectedIds.size;
  let counter=document.getElementById('map2-selected-count');
  if(!counter){counter=document.createElement('span');counter.id='map2-selected-count';counter.style.marginLeft='2px';document.querySelector('.onlySelected')?.appendChild(counter)}
  counter.textContent=total?`(${total.toLocaleString()})`:'';
  document.querySelectorAll('.category').forEach((el,i)=>{
    const cat=categories[i];if(!cat)return;
    const count=cat.points.reduce((n,p)=>n+(selectedIds.has(p.id)?1:0),0);
    const node=el.querySelector('.catCount');
    if(node)node.textContent=multiMode?(count?count.toLocaleString():''):cat.points.length.toLocaleString();
  });
}
const originalSyncSidebarSelection=syncSidebarSelection;
syncSidebarSelection=function(){originalSyncSidebarSelection();updateSelectionUi()};
const originalSetMultiMode=setMultiMode;
setMultiMode=function(on){originalSetMultiMode(on);updateSelectionUi()};
updateSelectionUi();

const originalRender=render;
render=function(){
  if(!dirty)return;
  dirty=false;
  gl.viewport(0,0,glCanvas.width,glCanvas.height);
  gl.clearColor(15/255,18/255,23/255,1);
  gl.clear(gl.COLOR_BUFFER_BIT);
  drawTerrain();
  updateGrid();
  drawRoads();
  drawPointsAndLabels();
  mapInfo.textContent=`Zoom ${camera.mpp.toFixed(3)} m/px · клетка ${gridWorldStep.toFixed(3)} м · roads ${roadsCount.toLocaleString()} · points ${allPoints.length.toLocaleString()} · cities ${categories.find(c=>c.key==='__cities')?.points.length?.toLocaleString()||'0'}`;
};

function updateTerrainButton(){
  const btn=document.getElementById('map2-terrain-toggle');
  if(btn)btn.textContent=terrainVisible?'Скрыть карту высот':'Показать карту высот';
}
function ensureToolsMenu(){
  const menu=document.querySelector('[data-menu="tools"]');
  if(!menu)return;
  let popup=document.getElementById('toolsPopup');
  if(!popup){
    popup=document.createElement('div');
    popup.id='toolsPopup';popup.className='menuPopup';popup.style.left='310px';
    popup.innerHTML='<button class="menuBtn" id="generateTerrain">Генерировать карту высот</button><button class="menuBtn" id="map2-terrain-toggle">Скрыть карту высот</button>';
    document.getElementById('topMenu')?.appendChild(popup);
  }else if(!document.getElementById('map2-terrain-toggle')){
    const b=document.createElement('button');b.className='menuBtn';b.id='map2-terrain-toggle';popup.appendChild(b);
  }
  const freshPopup=popup;
  menu.onclick=e=>{e.stopPropagation();freshPopup.classList.toggle('open');viewPopup?.classList.remove('open');updateTerrainButton()};
  document.getElementById('generateTerrain')?.addEventListener('click',e=>{e.stopPropagation();window.chrome?.webview?.postMessage('map2-generate-terrain')});
  document.getElementById('map2-terrain-toggle')?.addEventListener('click',e=>{e.stopPropagation();window.MapEditor2ToggleTerrain()});
  updateTerrainButton();
}
ensureToolsMenu();
loadTerrain();
