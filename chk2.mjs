const D=Math.PI/180,R2D=180/Math.PI,W=1920,H=1080;
const dot=(a,b)=>a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
const norm=v=>{const l=Math.hypot(...v);return v.map(x=>x/l)};
const cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
function rot(v,e){const h=e.h*2*Math.PI,p=e.p*2*Math.PI,r=e.r*2*Math.PI;
 const ch=Math.cos(h),sh=Math.sin(h),cp=Math.cos(p),sp=Math.sin(p),cr=Math.cos(r),sr=Math.sin(r);
 const rx=v[0]*cr-v[1]*sr,ry=v[0]*sr+v[1]*cr,rz=v[2];
 const px=rx,py=ry*cp-rz*sp,pz=ry*sp+rz*cp;
 return [px*ch+pz*sh,py,-px*sh+pz*ch];}
// РЕАЛЬНЫЕ данные 17:26
const cam=[139722.22,102.49,-58048.99];
const fwdReal=[-0.400,-0.171,-0.900], upReal=[-0.195,0.976,-0.099];
const O=[139724.00,100.17,-58048.00], N=[-0.1568,0.9872,-0.0282];
console.log('Реальные данные 17:26: N=('+N.join(', ')+')  наклон плоскости от вертикали = '+(Math.acos(Math.min(1,Math.abs(dot(norm(N),[0,1,0]))))*R2D).toFixed(2)+'°');
console.log('up камеры = ('+upReal.join(', ')+')  -> up.Y='+upReal[1]);
console.log('');
// Оси плоскости
let AU=norm(cross(N,[0,0,1])); if(!AU.every(isFinite)) AU=norm(cross(N,[1,0,0]));
let AV=norm(cross(N,AU));
function screenLines(f,u,r){
  const fh=(W/2)/Math.tan(80*D/2), fv=(H/2)/Math.tan(65*D/2), cx=W/2, cy=H/2;
  const yw=(x)=>{const rx=(x-cx)/fh; return cy+fv*(f[1]+r[1]*rx)/u[1];};
  const ds=(P)=>{const d=[P[0]-cam[0],P[1]-cam[1],P[2]-cam[2]];
    const z=dot(d,f); if(Math.abs(z)<1e-9)return null;
    return {u:cx+fh*(dot(d,r)/z), v:cy-fv*(dot(d,u)/z)};};
  const s=1e6;
  const P1=[O[0]+AU[0]*s+AV[0]*s,O[1]+AU[1]*s+AV[1]*s,O[2]+AU[2]*s+AV[2]*s];
  const P2=[O[0]-AU[0]*s+AV[0]*s,O[1]-AU[1]*s+AV[1]*s,O[2]-AU[2]*s+AV[2]*s];
  const d1=ds(P1),d2=ds(P2);
  let pl=null;
  if(d1&&d2){const k=(d1.v-d2.v)/(d1.u-d2.u); if(isFinite(k)) pl=[d1.v+k*(0-d1.u), d1.v+k*(W-d1.u)];}
  return {world:[yw(0),yw(W)], plane:pl};
}
console.log('=== РЕАЛЬНЫЙ базис камеры из лога (как есть), FOV 80/65 ===');
let L=screenLines(fwdReal,upReal,norm(cross(upReal,fwdReal)));
console.log('  голубой (мир):     y='+L.world.map(v=>v.toFixed(0)).join(', ')+'  наклон='+(Math.atan2(L.world[1]-L.world[0],W)*R2D).toFixed(2)+'°');
console.log('  БЕЛЫЙ (полотно):   '+(L.plane?('y='+L.plane.map(v=>v.toFixed(0)).join(', ')+'  наклон='+(Math.atan2(L.plane[1]-L.plane[0],W)*R2D).toFixed(2)+'°'):'нет линии (вне кадра)'));
console.log('');
console.log('=== наклон МИРОВОГО горизонта по режимам (кузов pitch=-5.49 roll=-8.05, голова -9.16) ===');
const tp=-5.49,tr=-8.05,hp=-9.16;
for(const [name,rollT] of [['0 (крен=0)',0],['1 (-roll)',-tr/360],['2 (+roll)',tr/360]]){
  let f=[0,0,-1],r=[1,0,0],u=[0,1,0];
  for(const s of [[0,hp/360,0],[0,0,0],[0,tp/360,rollT]]){
    f=rot(f,{h:s[0],p:s[1],r:s[2]}); r=rot(r,{h:s[0],p:s[1],r:s[2]}); u=rot(u,{h:s[0],p:s[1],r:s[2]});
  }
  f=norm(f); r=norm([r[0]-f[0]*dot(r,f),r[1]-f[1]*dot(r,f),r[2]-f[2]*dot(r,f)]);
  u=norm([u[0]-f[0]*dot(u,f)-r[0]*dot(u,r),u[1]-f[1]*dot(u,f)-r[1]*dot(u,r),u[2]-f[2]*dot(u,f)-r[2]*dot(u,r)]);
  const S=screenLines(f,u,r);
  console.log('  режим '+name.padEnd(12)+' наклон голубого='+(Math.atan2(S.world[1]-S.world[0],W)*R2D).toFixed(2).padStart(6)+'°  y(центр)='+((S.world[0]+S.world[1])/2).toFixed(0));
}
