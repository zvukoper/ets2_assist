(function () {
    'use strict';
    var ws=null, questState=null;
    var canvas=null, ctx=null;
    var cam={valid:false,x:0,y:0,z:0,fwd:{x:0,y:0,z:-1},right:{x:1,y:0,z:0},up:{x:0,y:1,z:0},fov:75,vfov:65,groundPlane:null};

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

    // ================================================================
    // v1.0.40.57: ПОЗИЦИЯ КАМЕРЫ ИЗ ЛЮБОГО ИСТОЧНИКА — БЕЗ NaN.
    //
    // ⛔ КОРЕНЬ «в AR исчезли иконки квестов» (был виден только прицел):
    // сглаженная поза ar_hud.js (window.__arSmooth.view) хранит позицию в полях
    // camX/camY/camZ, а собственная структура этого файла — в x/y/z. Код читал
    // ТОЛЬКО camPos.x/y/z, поэтому для общего view это давало undefined → NaN
    // в проекции → `sp.x>-90` ложно → маркер молча НЕ рисовался.
    // Нормализуем ОДИН раз на кадр в переиспользуемый объект (без аллокаций
    // на каждую точку).
    // ================================================================
    var _camPosOut={x:0,y:0,z:0};
    function viewPos(camPos){
        if(camPos){
            _camPosOut.x=(camPos.camX!==undefined)?camPos.camX:camPos.x;
            _camPosOut.y=(camPos.camY!==undefined)?camPos.camY:camPos.y;
            _camPosOut.z=(camPos.camZ!==undefined)?camPos.camZ:camPos.z;
            if(!Number.isFinite(_camPosOut.x)||!Number.isFinite(_camPosOut.y)||!Number.isFinite(_camPosOut.z)){
                _camPosOut.x=cam.x;_camPosOut.y=cam.y;_camPosOut.z=cam.z;
            }
            return _camPosOut;
        }
        _camPosOut.x=cam.x;_camPosOut.y=cam.y;_camPosOut.z=cam.z;
        return _camPosOut;
    }

    function project(p,yOverride,camPos){
        if(!cam.valid) return null;
        var vp=viewPos(camPos);
        var py=(typeof yOverride==='number' && Number.isFinite(yOverride)) ? yOverride : readNumber(p.Y,0);
        var rel={x:readNumber(p.X,0)-vp.x,y:py-vp.y,z:readNumber(p.Z,0)-vp.z};
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

    // ================================================================
    // v1.0.40.54: СГЛАЖИВАНИЕ ПОЗЫ КАМЕРЫ ДЛЯ КВЕСТОВЫХ МАРКЕРОВ.
    //
    // КОРЕНЬ «маркеры дрожат при движении»: поза применялась ТОЛЬКО в момент
    // прихода пакета телеметрии (~28–35 Гц), а кадров в 2 раза больше — между
    // пакетами маркер стоял, затем прыгал. Тот же дефект уже был у точек AR1
    // (v1.0.40.44) и решён ровно этой техникой: экстраполяция ЦЕЛИ по скорости
    // до текущего момента + экспоненциальный фильтр k = 1 − exp(−dt/tau).
    //
    // Сила сглаживания (tau) НЕ дублируется константой: берём её у ar_hud.js
    // (window.__arSmooth), чтобы оба слоя AR были сглажены ОДИНАКОВО. Если
    // ar_hud.js не загрузился — работает запасное значение 80 мс.
    // ================================================================
    function smoothCfg(){
        var h=window.__arSmooth;
        return (h&&h.cfg)||{enabled:true,posTau:.08,rotTau:.092,snapDistM:25,maxExtrapS:.15};
    }
    // Если ar_hud.js уже успел посчитать кадр — используем ЕГО сглаженную позу
    // (тогда оба слоя смотрят строго из одной точки и не «разъезжаются»).
    function smoothView(){
        var h=window.__arSmooth;
        var v=h&&h.view;
        return (v&&v.cameraValid)?v:null;
    }

    var camS={x:0,y:0,z:0,vx:0,vy:0,vz:0,valid:false,lastAt:0};
    var camPrevQ={x:0,y:0,z:0,atMs:0,valid:false};
    var telemetryAtMs=0;

    // Оценка скорости камеры — ВЫЗЫВАЕТСЯ ИЗ ОБРАБОТЧИКА ПАКЕТА (там точные
    // интервалы между выборками), не из кадра.
    function updateQuestCameraVelocity(){
        var cfg=smoothCfg();
        var now=performance.now();
        if(camPrevQ.valid){
            var dt=(now-camPrevQ.atMs)/1000;
            if(dt>.005&&dt<.5){
                var dxp=cam.x-camPrevQ.x,dyp=cam.y-camPrevQ.y,dzp=cam.z-camPrevQ.z;
                var dist=Math.hypot(dxp,dyp,dzp);
                if(dist<(cfg.snapDistM||25)){
                    var nvx=dxp/dt,nvy=dyp/dt,nvz=dzp/dt,a=.35;
                    camS.vx+=(nvx-camS.vx)*a;
                    camS.vy+=(nvy-camS.vy)*a;
                    camS.vz+=(nvz-camS.vz)*a;
                }else{camS.vx=camS.vy=camS.vz=0;}
            }
        }
        camPrevQ.x=cam.x;camPrevQ.y=cam.y;camPrevQ.z=cam.z;
        camPrevQ.atMs=now;camPrevQ.valid=true;
        telemetryAtMs=now;
    }
    function resetQuestSmoothing(){camS.valid=false;camS.vx=camS.vy=camS.vz=0;camPrevQ.valid=false;}

    // Пересчёт сглаженной позы на КАЖДЫЙ кадр. Возвращает null, если сглаживание
    // выключено или поза ещё не получена — тогда проекция идёт по сырым данным.
    function smoothQuestCamera(nowMs){
        var cfg=smoothCfg();
        if(cfg.enabled===false||!cam.valid) return null;

        if(!camS.valid){
            camS.x=cam.x;camS.y=cam.y;camS.z=cam.z;
            camS.vx=camS.vy=camS.vz=0;
            camS.valid=true;camS.lastAt=nowMs;
            return camS;
        }
        var dt=(nowMs-camS.lastAt)/1000;
        if(!Number.isFinite(dt)||dt<=0) dt=1/60;
        if(dt>.25) dt=.25;
        camS.lastAt=nowMs;

        var jump=Math.hypot(cam.x-camS.x,cam.y-camS.y,cam.z-camS.z);
        if(jump>(cfg.snapDistM||25)){
            camS.x=cam.x;camS.y=cam.y;camS.z=cam.z;
            camS.vx=camS.vy=camS.vz=0;
            return camS;
        }

        var kPos=1-Math.exp(-dt/(cfg.posTau||.08));
        var age=Math.max(0,Math.min((nowMs-(telemetryAtMs||nowMs))/1000,cfg.maxExtrapS||.15));
        camS.x+=((cam.x+camS.vx*age)-camS.x)*kPos;
        camS.y+=((cam.y+camS.vy*age)-camS.y)*kPos;
        camS.z+=((cam.z+camS.vz*age)-camS.z)*kPos;
        return camS;
    }
    function markerChar(m){return m==='yellow_exclamation'?'!':(m==='yellow_question'||m==='gray_question'?'?':'');}
    function markerColors(m){return m==='gray_question'?{fill:'#aeb4bf',stroke:'rgba(28,33,39,.98)',shadow:'rgba(220,225,232,.55)'}:{fill:'#ffd21f',stroke:'rgba(74,55,0,.98)',shadow:'rgba(255,210,30,.80)'};}

    // ================================================================
    // v1.0.40.56: ИКОНКИ КВЕСТОВЫХ МАРКЕРОВ (растровые, 32x32).
    //
    // Соответствие имён и состояний:
    //   Pointer_quest_on/off      — восклицательный знак «!» (доступный квест);
    //   Pointer_questdone_on/off  — вопросительный знак «?» (шаг квеста/сдача);
    //   _on = жёлтый (активный), _off = серый (неактивный).
    // В AR масштаб 1:1 (32x32), размер — из того же markerStyle (s.size),
    // что и раньше, чтобы дистанционное масштабирование не менялось.
    //
    // ⚠️ ВАЖНО: пока картинка не загрузилась, рисуем ТЕКСТОВЫЙ глиф — иначе
    // маркер на первом кадре исчезнет. Раньше глиф рисовался ВСЕГДА; при ошибке
    // загрузки он остаётся и теперь (см. onerror).
    // ================================================================
    var markerImgCache={};
    function markerPng(m){
        if(m==='yellow_exclamation')return{on:'Pointer_quest_on_32x32.png',off:'Pointer_quest_off_32x32.png'};
        if(m==='yellow_question')return{on:'Pointer_questdone_on_32x32.png',off:'Pointer_questdone_off_32x32.png'};
        if(m==='gray_question')return{on:'Pointer_questdone_off_32x32.png',off:'Pointer_questdone_off_32x32.png'};
        return null;
    }
    function markerImage(m){
        var names=markerPng(m); if(!names) return null;
        // gray_question — ВСЕГДА выключенный вариант (серый), независимо от яркости.
        var name=m==='gray_question'?names.off:names.on;
        if(markerImgCache[name]!==undefined) return markerImgCache[name];
        var img=new Image();
        img.onerror=function(){ markerImgCache[name]=null; };
        img.src='quests/images/'+name;
        markerImgCache[name]=img;
        return img;
    }
    function drawMarkerBitmap(img,screen,size,alpha){
        var isz=32*(size/10);          // 32 px при базовом размере 10
        if(isz<8)isz=8; if(isz>96)isz=96;
        ctx.save();
        ctx.globalAlpha=alpha;
        ctx.shadowColor='rgba(0,0,0,.55)';ctx.shadowBlur=4;
        ctx.drawImage(img,screen.x-isz/2,screen.y-isz/2,isz,isz);
        ctx.restore();
    }

    function drawMarker(p,screen,dist){
        var s=markerStyle(dist); if(s.alpha<=0) return;
        var img=markerImage(p.Marker);
        if(img && img.complete && img.naturalWidth>0){
            drawMarkerBitmap(img,screen,s.size,s.alpha);
            return;
        }
        // Запасной путь: текстовый глиф (пока картинка грузится или при ошибке).
        var ch=markerChar(p.Marker); if(!ch) return;
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
    function drawOffscreen(p,dist,yOverride,camPos){
        if(!p.ArOffscreenPointer) return;
        var sp=project(p,yOverride,camPos); if(sp && sp.x>=0 && sp.x<=innerWidth && sp.y>=0 && sp.y<=innerHeight) return;
        var py=(typeof yOverride==='number' && Number.isFinite(yOverride)) ? yOverride : readNumber(p.Y,0);
        var vp=viewPos(camPos);
        var rel={x:readNumber(p.X,0)-vp.x,y:py-vp.y,z:readNumber(p.Z,0)-vp.z};
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
        // v1.0.40.56: указатель за краем экрана — тоже новая иконка (8x8-масштаб
        // повёрнут по направлению), а не стрелка-ромб с текстом.
        var img=markerImage(p.Marker);
        if(img && img.complete && img.naturalWidth>0){
            var isz=26;
            ctx.save();ctx.globalAlpha=opacity;ctx.translate(x,y);ctx.rotate(angle);
            ctx.shadowColor='rgba(0,0,0,.55)';ctx.shadowBlur=5;
            ctx.drawImage(img,-isz/2,-isz/2,isz,isz);
            ctx.restore();
            return;
        }
        ctx.save();ctx.globalAlpha=opacity;ctx.translate(x,y);ctx.rotate(angle);
        ctx.fillStyle=c.fill;ctx.strokeStyle=c.stroke;ctx.lineWidth=2;ctx.shadowColor=c.shadow;ctx.shadowBlur=5;
        ctx.beginPath();ctx.moveTo(13,0);ctx.lineTo(-8,-8);ctx.lineTo(-4,0);ctx.lineTo(-8,8);ctx.closePath();ctx.fill();ctx.stroke();
        ctx.rotate(-angle);ctx.font='700 13px Segoe UI,Arial,sans-serif';ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillStyle=p.Marker==='gray_question'?'#22272d':'#3b2b00';ctx.fillText(markerChar(p.Marker),0,0);
        ctx.restore();
    }

    // Высота метки над землёй берётся от СГЛАЖЕННОЙ позы, не от сырой:
    // иначе eyeHeight «дышит» вместе с пакетами и маркер дрожит по вертикали.
    // Координаты — СКАЛЯРАМИ (вызывающий уже разрешил позу): не тянем ссылку
    // на общий буфер viewPos, иначе вложенный вызов его перезапишет.
    function eyeHeightAboveGroundAt(cx,cy,cz){
        if(!cam.valid || !cam.groundPlane) return 0;
        var gp=cam.groundPlane;
        if(gp.valid===false) return 0;
        if(!Number.isFinite(cx)||!Number.isFinite(cy)||!Number.isFinite(cz)) return 0;
        var groundY=Number(gp.referenceHeight);
        var n=gp.normal, o=gp.origin;
        if(Array.isArray(n) && n.length>=3 && Array.isArray(o) && o.length>=3){
            var nx=Number(n[0]), ny=Number(n[1]), nz=Number(n[2]);
            var ox=Number(o[0]), oy=Number(o[1]), oz=Number(o[2]);
            if([nx,ny,nz,ox,oy,oz].every(Number.isFinite) && Math.abs(ny)>1e-7){
                groundY=oy-(nx*(cx-ox)+nz*(cz-oz))/ny;
            }
        }

        if(!Number.isFinite(groundY)) return 0;
        var h=cy-groundY;
        return Number.isFinite(h) && h>0 ? h : 0;
    }

    function draw(){
        ensureCanvas();
        ctx.clearRect(0,0,innerWidth,innerHeight);
        var pts=questState&&Array.isArray(questState.points)?questState.points:[];
        if(cam.valid){
            // v1.0.40.54: та же техника, что и для точек AR1 — экстраполяция цели
            // + экспоненциальный фильтр. Сначала пробуем готовую сглаженную позу
            // ar_hud.js (оба слоя смотрят из ОДНОЙ точки), иначе считаем сами.
            var camPos=smoothQuestCamera(performance.now());
            var vp=viewPos(camPos);
            var vpx=vp.x, vpy=vp.y, vpz=vp.z;   // скаляры: не держим ссылку на общий буфер
            var eyeH=eyeHeightAboveGroundAt(vpx,vpy,vpz);
            pts.forEach(function(p){
                if(!p || !p.ArVisible || !p.Marker || p.Marker==='none') return;

                // Квестовая метка должна быть не на поверхности земли, а на высоте
                // глаз игрока. Высота берётся из ФАКТИЧЕСКОЙ 6DoF-позы камеры:
                // camera Y − Y земли под камерой. Поэтому фура, легковая машина,
                // автобус и т.п. автоматически дают свою высоту метки.
                var markerY=readNumber(p.Y,0)+eyeH;
                var rel={x:readNumber(p.X,0)-vpx,y:markerY-vpy,z:readNumber(p.Z,0)-vpz};
                var dist=len3(rel);
                var sp=project(p,markerY,camPos);
                if(sp && sp.x>-90 && sp.x<innerWidth+90 && sp.y>-90 && sp.y<innerHeight+90) drawMarker(p,sp,dist);
                if(p.ArOffscreenPointer) drawOffscreen(p,dist,markerY,camPos);
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
            ws.onmessage=function(ev){try{var d=JSON.parse(ev.data);if(d.command==='quest_state'){questState=d;applyPauseFade(d.interactive===true);}}catch(e){}};
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
                    cam.groundPlane=d.groundPlane||null;
                    cam.valid=c.valid===undefined?true:!!c.valid;
                    // v1.0.40.54: скорость камеры — из обработчика ПАКЕТА (точные
                    // интервалы), иначе экстраполяция цели не сглаживает, а дрожит.
                    updateQuestCameraVelocity();
                }catch(e){}
            };
            tws.onclose=function(){setTimeout(connectTelemetry,1500)};tws.onerror=function(){try{tws.close()}catch(e){}};
        }catch(e){setTimeout(connectTelemetry,1500)}
    }
    window.addEventListener('resize',resize);
    document.addEventListener('DOMContentLoaded',function(){ensureCanvas();connectQuest();connectTelemetry();applyPauseFade(false);requestAnimationFrame(draw);});})();
