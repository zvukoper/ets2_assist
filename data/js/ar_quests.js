(function () {
    'use strict';
    var ws=null, questState=null;
    var canvas=null, ctx=null;
    var cam={valid:false,x:0,y:0,z:0,fwd:{x:0,y:0,z:-1},right:{x:1,y:0,z:0},up:{x:0,y:1,z:0},fov:75,vfov:65};

    function ensureCanvas(){
        if(canvas) return;
        canvas=document.createElement('canvas');
        canvas.id='questArCanvas';
        canvas.style.cssText='position:fixed;inset:0;width:100vw;height:100vh;pointer-events:none;z-index:240';
        document.body.appendChild(canvas);
        ctx=canvas.getContext('2d');
        resize();
    }
    function resize(){
        if(!canvas) return;
        var d=window.devicePixelRatio||1;
        canvas.width=Math.round(innerWidth*d);
        canvas.height=Math.round(innerHeight*d);
        canvas.style.width=innerWidth+'px';
        canvas.style.height=innerHeight+'px';
        ctx.setTransform(d,0,0,d,0,0);
    }
    function readNumber(v, fallback){
        var n=Number(v);
        return Number.isFinite(n)?n:fallback;
    }
    function readVec3(v, fallback){
        if(Array.isArray(v) && v.length>=3){
            return {x:readNumber(v[0],fallback.x),y:readNumber(v[1],fallback.y),z:readNumber(v[2],fallback.z)};
        }
        if(v && typeof v==='object'){
            return {x:readNumber(v.x,fallback.x),y:readNumber(v.y,fallback.y),z:readNumber(v.z,fallback.z)};
        }
        return {x:fallback.x,y:fallback.y,z:fallback.z};
    }
    function dot(a,b){return a.x*b.x+a.y*b.y+a.z*b.z;}
    function len3(v){return Math.sqrt(v.x*v.x+v.y*v.y+v.z*v.z)||1;}
    function project(p){
        if(!cam.valid) return null;
        var rel={x:readNumber(p.X,0)-cam.x,y:readNumber(p.Y,0)-cam.y,z:readNumber(p.Z,0)-cam.z};
        var depth=dot(rel,cam.fwd);
        if(depth<=0.05) return null;
        var sx=dot(rel,cam.right), sy=dot(rel,cam.up);
        var hfov=readNumber(cam.fov,75)*Math.PI/180;
        var vfov=readNumber(cam.vfov,65)*Math.PI/180;
        return {
            x:innerWidth/2+(sx/depth)/Math.tan(hfov/2)*innerWidth/2,
            y:innerHeight/2-(sy/depth)/Math.tan(vfov/2)*innerHeight/2,
            depth:depth
        };
    }
    function markerStyle(dist){
        var s=(questState&&questState.settings)||{};
        var near=Number(s.ArSizeMaxDistanceM||10),far=Number(s.ArSizeMinDistanceM||500);
        var fadeS=Number(s.ArFadeStartDistanceM||500),fadeE=Number(s.ArFadeEndDistanceM||1500);
        var max=Number(s.ArMaxPointSizePx||10),min=Number(s.ArMinPointSizePx||3);
        var t=dist<=near?0:dist>=far?1:(dist-near)/(far-near);
        var size=max+(min-max)*t;
        var a=dist<=fadeS?1:dist>=fadeE?0:1-(dist-fadeS)/(fadeE-fadeS);
        var outline=Number(s.ArNearOutlinePx||3)+(Number(s.ArFarOutlinePx||1)-Number(s.ArNearOutlinePx||3))*Math.max(0,Math.min(1,t));
        return {size:Math.max(2,size),alpha:Math.max(0,Math.min(1,a)),outline:Math.max(.5,outline)};
    }
    function markerChar(m){return m==='yellow_exclamation'?'!':(m==='yellow_question'||m==='gray_question'?'?':'');}
    function markerColors(m){return m==='gray_question'?{fill:'#aeb4bf',stroke:'rgba(28,33,39,.98)',shadow:'rgba(220,225,232,.55)'}:{fill:'#ffd21f',stroke:'rgba(74,55,0,.98)',shadow:'rgba(255,210,30,.80)'};}

    function drawMarker(p,screen,dist){
        var ch=markerChar(p.Marker); if(!ch) return;
        var s=markerStyle(dist); if(s.alpha<=0) return;
        var c=markerColors(p.Marker);
        ctx.save();
        ctx.globalAlpha=s.alpha;
        ctx.font='700 '+Math.max(9,s.size*1.9)+'px Segoe UI,Arial,sans-serif';
        ctx.textAlign='center';ctx.textBaseline='middle';
        ctx.lineWidth=s.outline;
        ctx.strokeStyle=c.stroke;ctx.fillStyle=c.fill;ctx.shadowColor=c.shadow;ctx.shadowBlur=4;
        ctx.strokeText(ch,screen.x,screen.y);ctx.fillText(ch,screen.x,screen.y);
        ctx.restore();
    }
    function drawOffscreen(p,dist){
        if(!p.ArOffscreenPointer) return;
        var sp=project(p); if(sp && sp.x>=0 && sp.x<=innerWidth && sp.y>=0 && sp.y<=innerHeight) return;
        var rel={x:readNumber(p.X,0)-cam.x,y:readNumber(p.Y,0)-cam.y,z:readNumber(p.Z,0)-cam.z};
        var sx=dot(rel,cam.right), sy=dot(rel,cam.up);
        var dx=sx,dy=-sy;
        if(Math.abs(dx)+Math.abs(dy)<0.001){dx=0;dy=1;}
        var length=Math.hypot(dx,dy)||1;dx/=length;dy/=length;
        var margin=Math.max(22,Math.min(innerWidth,innerHeight)*0.035);
        var cx=innerWidth/2,cy=innerHeight/2;
        var tx=dx>0?(innerWidth-margin-cx)/dx:(margin-cx)/dx;
        var ty=dy>0?(innerHeight-margin-cy)/dy:(margin-cy)/dy;
        var t=Math.min(Math.abs(tx)||Infinity,Math.abs(ty)||Infinity);
        if(!isFinite(t)||t<=0)t=1;
        var x=cx+dx*t,y=cy+dy*t;
        x=Math.max(margin,Math.min(innerWidth-margin,x));y=Math.max(margin,Math.min(innerHeight-margin,y));
        var angle=Math.atan2(dy,dx), c=markerColors(p.Marker), opacity=markerStyle(dist).alpha;
        if(opacity<=0)return;
        ctx.save();ctx.globalAlpha=opacity;ctx.translate(x,y);ctx.rotate(angle);
        ctx.fillStyle=c.fill;ctx.strokeStyle=c.stroke;ctx.lineWidth=2;ctx.shadowColor=c.shadow;ctx.shadowBlur=5;
        ctx.beginPath();ctx.moveTo(13,0);ctx.lineTo(-8,-8);ctx.lineTo(-4,0);ctx.lineTo(-8,8);ctx.closePath();ctx.fill();ctx.stroke();
        ctx.rotate(-angle);ctx.font='700 13px Segoe UI,Arial,sans-serif';ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillStyle=p.Marker==='gray_question'?'#22272d':'#3b2b00';ctx.fillText(markerChar(p.Marker),0,0);
        ctx.restore();
    }

    function draw(){
        ensureCanvas();
        ctx.clearRect(0,0,innerWidth,innerHeight);
        var pts=questState&&Array.isArray(questState.points)?questState.points:[];
        if(cam.valid){
            pts.forEach(function(p){
                if(!p || !p.ArVisible || !p.Marker || p.Marker==='none') return;
                var rel={x:readNumber(p.X,0)-cam.x,y:readNumber(p.Y,0)-cam.y,z:readNumber(p.Z,0)-cam.z};
                var dist=len3(rel);
                var sp=project(p);
                if(sp && sp.x>-90 && sp.x<innerWidth+90 && sp.y>-90 && sp.y<innerHeight+90) drawMarker(p,sp,dist);
                if(p.ArOffscreenPointer) drawOffscreen(p,dist);
            });
        }
        requestAnimationFrame(draw);
    }

    function applyPauseFade(paused){
        var value=paused?'0':'1';
        try{
            document.documentElement.style.setProperty('transition','opacity 150ms linear','important');
            document.body.style.setProperty('transition','opacity 150ms linear','important');
            document.documentElement.style.setProperty('opacity',value,'important');
            document.body.style.setProperty('opacity',value,'important');
        }catch(e){}
    }
    function connectQuest(){
        try{
            ws=new WebSocket('ws://localhost:8085/');
            ws.onmessage=function(ev){try{var d=JSON.parse(ev.data);if(d.command==='quest_state'){questState=d;applyPauseFade(d.paused===true);}}catch(e){}};
            ws.onclose=function(){setTimeout(connectQuest,1500)};
            ws.onerror=function(){try{ws.close()}catch(e){}};
        }catch(e){setTimeout(connectQuest,1500)}
    }
    function connectTelemetry(){
        try{
            var tws=new WebSocket('ws://localhost:8084/');
            tws.onmessage=function(ev){
                try{
                    var d=JSON.parse(ev.data);if(d.command!=='ar_telemetry'||!d.camera)return;
                    var c=d.camera;
                    var p=readVec3(c.position,{x:0,y:0,z:0});
                    var f=readVec3(c.forward,{x:0,y:0,z:-1});
                    var r=readVec3(c.right,{x:1,y:0,z:0});
                    var u=readVec3(c.up,{x:0,y:1,z:0});
                    cam.x=p.x;cam.y=p.y;cam.z=p.z;
                    cam.fwd=f;cam.right=r;cam.up=u;
                    cam.fov=readNumber(c.fovDeg,75);cam.vfov=readNumber(c.fovDegVertical,65);
                    cam.valid=c.valid===undefined?true:!!c.valid;
                }catch(e){}
            };
            tws.onclose=function(){setTimeout(connectTelemetry,1500)};tws.onerror=function(){try{tws.close()}catch(e){}};
        }catch(e){setTimeout(connectTelemetry,1500)}
    }
    window.addEventListener('resize',resize);
    document.addEventListener('DOMContentLoaded',function(){ensureCanvas();connectQuest();connectTelemetry();applyPauseFade(false);requestAnimationFrame(draw);});
})();