(()=>{
'use strict';

// Map Editor 2 final interaction/layer/UI fixes.

// The WebGL canvas must not cover the terrain with an opaque framebuffer. C# also
// changes the WebGL context to alpha:true and clearColor(..., 0).
if(typeof glCanvas!=='undefined'){
  glCanvas.style.pointerEvents='auto';
}
if(typeof gridCanvas!=='undefined'){
  gridCanvas.style.pointerEvents='none';
}
if(typeof labelCanvas!=='undefined'){
  labelCanvas.style.pointerEvents='none';
}
if(typeof terrainCanvas!=='undefined'&&terrainCanvas){
  terrainCanvas.style.pointerEvents='none';
}

// Restore natural mouse-wheel zoom direction: wheel down zooms out, wheel up zooms in.
if(typeof glCanvas!=='undefined'&&glCanvas){
  glCanvas.addEventListener('wheel',e=>{
    e.preventDefault();
    e.stopImmediatePropagation();
    const before=screenToWorld(e.clientX,e.clientY);
    const factor=Math.pow(1.13,-e.deltaY/100);
    camera.mpp=Math.max(limits.minMpp,Math.min(limits.maxMpp,camera.mpp*factor));
    const after=screenToWorld(e.clientX,e.clientY);
    camera.x+=before.x-after.x;
    camera.z+=before.z-after.z;
    dirty=true;
    requestFrame();
  },{capture:true,passive:false});
}

// Fit on startup only to loaded points, never to road bounds.
function fitToPointsOnly(){
  try{
    const pts=(typeof getVisiblePoints==='function'?getVisiblePoints():[]).filter(p=>p&&!p.isCity&&Number.isFinite(Number(p.x))&&Number.isFinite(Number(p.z)));
    if(!pts.length)return false;
    let minX=Infinity,maxX=-Infinity,minZ=Infinity,maxZ=-Infinity;
    for(const p of pts){const x=Number(p.x),z=Number(p.z);minX=Math.min(minX,x);maxX=Math.max(maxX,x);minZ=Math.min(minZ,z);maxZ=Math.max(maxZ,z)}
    const s=mapSize();
    const dx=Math.max(1,maxX-minX),dz=Math.max(1,maxZ-minZ);
    camera.x=(minX+maxX)*.5;
    camera.z=(minZ+maxZ)*.5;
    camera.mpp=Math.max(limits.minMpp,Math.min(limits.maxMpp,Math.max(dx/(s.w*.82),dz/(s.h*.82))));
    dirty=true;
    requestFrame();
    return true;
  }catch{return false}
}
setTimeout(()=>fitToPointsOnly(),450);
setTimeout(()=>fitToPointsOnly(),1200);
setTimeout(()=>fitToPointsOnly(),2200);

// Font controls: one click changes font size by 3% instead of 1%.
function patchFontButton(id,delta){
  const btn=document.getElementById(id);
  if(!btn||btn.dataset.map2FontPatched)return;
  btn.dataset.map2FontPatched='1';
  btn.addEventListener('click',e=>{
    e.preventDefault();
    e.stopImmediatePropagation();
    fontScale=Math.max(.75,Math.min(2,fontScale+delta));
    document.documentElement.style.setProperty('--font-scale',fontScale.toFixed(2));
    dirty=true;
    requestFrame();
  },{capture:true});
}
patchFontButton('fontPlus',.03);
patchFontButton('fontMinus',-.03);

// Status-bar controls: keep all checkboxes on the left and terrain path on the right.
let citiesVisible=true;
function ensureCityToggle(){
  if(!status)return;
  let input=document.getElementById('map2-show-cities');
  if(!input){
    const label=document.createElement('label');
    label.className='onlySelected';
    label.style.marginLeft='14px';
    label.innerHTML='<input id="map2-show-cities" type="checkbox" checked>города';
    const terrain= document.getElementById('map2-terrain-status');
    if(terrain)status.insertBefore(label,terrain);else status.appendChild(label);
    input=label.querySelector('input');
    input.addEventListener('change',()=>{
      citiesVisible=input.checked;
      const cityCat=categories.find(c=>c.key==='__cities');
      if(cityCat)cityCat.visible=citiesVisible;
      dirty=true;
      requestFrame();
    });
  }
  input.checked=citiesVisible;
}
function removeCityVisibilityEye(){
  try{
    categories.forEach((cat,i)=>{
      if(cat.key!=='__cities')return;
      const el=categoryList.children[i];
      const eye=el?.querySelector('.eye');
      if(eye)eye.remove();
    });
  }catch{}
}
if(typeof categoryList!=='undefined'&&categoryList){
  const observer=new MutationObserver(()=>{ensureCityToggle();removeCityVisibilityEye()});
  observer.observe(categoryList,{childList:true,subtree:true});
}
ensureCityToggle();
removeCityVisibilityEye();

// Category names, counters and visibility icons receive the same dark shadow.
if(typeof document!=='undefined'){
  const style=document.createElement('style');
  style.id='map2-category-shadows-final';
  style.textContent=`
#categoryList .categoryHead,
#categoryList .catCount,
#categoryList .eye{
  text-shadow:0 0 4px rgba(0,0,0,.95),0 0 7px rgba(0,0,0,.70);
}
#map2-terrain-status{margin-left:auto!important;}
#map2-terrain-status a{text-shadow:0 0 3px rgba(0,0,0,.85);}
`;
  document.head.appendChild(style);
}

// Force the final requested logical layer order in the DOM as well.
if(typeof terrainCanvas!=='undefined'&&terrainCanvas)terrainCanvas.style.zIndex='0';
if(typeof glCanvas!=='undefined'&&glCanvas)glCanvas.style.zIndex='1';
if(typeof gridCanvas!=='undefined'&&gridCanvas)gridCanvas.style.zIndex='2';
if(typeof labelCanvas!=='undefined'&&labelCanvas)labelCanvas.style.zIndex='3';

// City checkbox is the visibility switch for the synthetic "Города" category.
const oldRender=typeof render==='function'?render:null;
if(oldRender){
  render=function(){
    if(oldRender)oldRender();
    if(!citiesVisible){
      // The point renderer already filters by category visibility; this keeps the
      // synthetic category state synchronized after category rebuilds.
      const cityCat=categories.find(c=>c.key==='__cities');
      if(cityCat)cityCat.visible=false;
    }
  };
}

// After each category rebuild make sure the synthetic city category cannot acquire
// a visibility eye of its own and that the status controls remain in their place.
const oldSyncSidebarSelection=typeof syncSidebarSelection==='function'?syncSidebarSelection:null;
if(oldSyncSidebarSelection){
  syncSidebarSelection=function(){oldSyncSidebarSelection();ensureCityToggle();removeCityVisibilityEye();};
}
})();
