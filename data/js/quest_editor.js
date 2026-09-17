(function(){
'use strict';
var pts=[]; var c=null,ctx=null;
function make(){
  if(c)return; c=document.createElement('canvas'); c.id='questEditorOverlay'; c.style.cssText='position:fixed;inset:0;width:100vw;height:100vh;z-index:60;pointer-events:none'; document.body.appendChild(c); ctx=c.getContext('2d'); resize(); window.addEventListener('resize',resize);
}
function resize(){if(!c)return;var d=devicePixelRatio||1;c.width=Math.round(innerWidth*d);c.height=Math.round(innerHeight*d);ctx.setTransform(d,0,0,d,0,0);}
function pointScreen(p){try{return typeof worldToScreen==='function'?worldToScreen({x:Number(p.X),z:Number(p.Z)}):null}catch(e){return null}}
function draw(){
  make(); ctx.clearRect(0,0,innerWidth,innerHeight);
  pts.forEach(function(p){if(!p.Interactive)return;var q=pointScreen(p);if(!q)return;if(q.x<-30||q.y<-30||q.x>innerWidth+30||q.y>innerHeight+30)return;ctx.save();ctx.globalAlpha=.95;ctx.beginPath();ctx.arc(q.x,q.y,8,0,Math.PI*2);ctx.lineWidth=3;ctx.strokeStyle='#ffd21f';ctx.shadowColor='rgba(255,210,31,.8)';ctx.shadowBlur=5;ctx.stroke();ctx.restore();});
}
function connect(){try{var ws=new WebSocket('ws://localhost:8085/');ws.onmessage=function(ev){try{var d=JSON.parse(ev.data);if(d.command==='quest_state'){pts=d.points||[];draw();}}catch(e){}};ws.onclose=function(){setTimeout(connect,1500)};ws.onerror=function(){try{ws.close()}catch(e){}}}catch(e){setTimeout(connect,1500)}}
function loop(){draw();requestAnimationFrame(loop)}
make();connect();requestAnimationFrame(loop);
})();