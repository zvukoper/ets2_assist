(function () {
    'use strict';
    var ws = null;
    var points = [];
    var settings = {};
    var cam = { valid:false, x:0, y:0, z:0, fwd:{x:0,y:0,z:-1}, right:{x:1,y:0,z:0}, up:{x:0,y:1,z:0}, fov:75 };
    var canvas = document.getElementById('arCanvas');
    if (!canvas) return;
    var ctx = canvas.getContext('2d');

    function connect() {
        try {
            ws = new WebSocket('ws://localhost:8085/');
            ws.onmessage = function (ev) {
                try {
                    var d = JSON.parse(ev.data);
                    if (d.command === 'quest_state') { points = (d.points || []).filter(function(p){return p.ArVisible;}); settings = d.settings || {}; }
                } catch(e){}
            };
            ws.onclose = function(){setTimeout(connect,1500);};
            ws.onerror = function(){try{ws.close();}catch(e){}};
        } catch(e){setTimeout(connect,1500);}
    }

    function connectTelemetry() {
        try {
            var tws = new WebSocket('ws://localhost:8084/');
            tws.onmessage = function (ev) {
                try {
                    var d = JSON.parse(ev.data);
                    if (d.command !== 'ar_telemetry' || !d.camera) return;
                    var c = d.camera, p = c.position || {}, f = c.forward || {}, r = c.right || {}, u = c.up || {};
                    cam.x = Number(p.x)||0; cam.y=Number(p.y)||0; cam.z=Number(p.z)||0;
                    cam.fwd={x:Number(f.x)||0,y:Number(f.y)||0,z:Number(f.z)||-1};
                    cam.right={x:Number(r.x)||1,y:Number(r.y)||0,z:Number(r.z)||0};
                    cam.up={x:Number(u.x)||0,y:Number(u.y)||1,z:Number(u.z)||0};
                    cam.fov=Number(c.fovDeg)||75; cam.valid=!!c.valid;
                } catch(e){}
            };
            tws.onclose=function(){setTimeout(connectTelemetry,1500);};
            tws.onerror=function(){try{tws.close();}catch(e){}};
        } catch(e){setTimeout(connectTelemetry,1500);}
    }

    function dprod(a,b){return a.x*b.x+a.y*b.y+a.z*b.z;}
    function resize(){ var d=window.devicePixelRatio||1; canvas.width=Math.round(innerWidth*d); canvas.height=Math.round(innerHeight*d); canvas.style.width=innerWidth+'px'; canvas.style.height=innerHeight+'px'; ctx.setTransform(d,0,0,d,0,0); }
    function project(p){
        var rel={x:Number(p.X)-cam.x,y:Number(p.Y)-cam.y,z:Number(p.Z)-cam.z};
        var depth=dprod(rel,cam.fwd);
        if(depth<=0.05)return null;
        var sx=dprod(rel,cam.right), sy=dprod(rel,cam.up);
        var hfov=(Number(cam.fov)||75)*Math.PI/180, vfov=65*Math.PI/180;
        return {x:innerWidth/2+(sx/depth)/Math.tan(hfov/2)*innerWidth/2, y:innerHeight/2-(sy/depth)/Math.tan(vfov/2)*innerHeight/2};
    }
    function markerStyle(p,dist){
        var near=Number(settings.ArSizeMaxDistanceM||10), far=Number(settings.ArSizeMinDistanceM||500), fadeS=Number(settings.ArFadeStartDistanceM||500), fadeE=Number(settings.ArFadeEndDistanceM||1500);
        var max=Number(settings.ArMaxPointSizePx||10), min=Number(settings.ArMinPointSizePx||3);
        var t=dist<=near?0:dist>=far?1:(dist-near)/(far-near); var size=max+(min-max)*t;
        var fa=dist<=fadeS?1:(dist>=fadeE?0:1-(dist-fadeS)/(fadeE-fadeS));
        var outline=Number(settings.ArNearOutlinePx||3)+(Number(settings.ArFarOutlinePx||1)-Number(settings.ArNearOutlinePx||3))*Math.min(1,Math.max(0,t));
        return {size:Math.max(2,size),alpha:Math.max(0,Math.min(1,fa)),outline:Math.max(.5,outline)};
    }
    function iconText(marker){return marker==='yellow_exclamation'?'!':(marker==='yellow_question'||marker==='gray_question'?'?':'');}
    function draw(){
        ctx.clearRect(0,0,innerWidth,innerHeight);
        if(!cam.valid || !points.length) {requestAnimationFrame(draw);return;}
        points.forEach(function(p){
            var q=project(p); if(!q) return; if(q.x < -80 || q.x>innerWidth+80 || q.y<-80 || q.y>innerHeight+80) return;
            var dx=Number(p.X)-cam.x,dy=Number(p.Y)-cam.y,dz=Number(p.Z)-cam.z,dist=Math.sqrt(dx*dx+dy*dy+dz*dz);
            var s=markerStyle(p,dist); if(s.alpha<=0) return;
            var ch=iconText(p.Marker); if(!ch)return;
            var yellow=p.Marker!=='gray_question';
            ctx.save(); ctx.globalAlpha=s.alpha;
            ctx.font='700 '+Math.max(9,s.size*1.9)+'px Segoe UI, Arial'; ctx.textAlign='center'; ctx.textBaseline='middle';
            ctx.lineWidth=s.outline; ctx.strokeStyle=yellow?'rgba(95,66,0,.95)':'rgba(32,37,43,.95)'; ctx.fillStyle=yellow?'#ffd21f':'#aeb4bf';
            ctx.shadowColor=yellow?'rgba(255,210,35,.75)':'rgba(210,215,225,.45)'; ctx.shadowBlur=4;
            ctx.strokeText(ch,q.x,q.y); ctx.fillText(ch,q.x,q.y); ctx.restore();
        });
        requestAnimationFrame(draw);
    }
    window.addEventListener('resize',resize);
    resize(); connect(); connectTelemetry(); requestAnimationFrame(draw);
})();