function rot(v,o,negRoll){
  const r0 = negRoll ? -o.r : o.r;
  const h=o.h*2*Math.PI,p=o.p*2*Math.PI,r=r0*2*Math.PI;
  const ch=Math.cos(h),sh=Math.sin(h),cp=Math.cos(p),sp=Math.sin(p),cr=Math.cos(r),sr=Math.sin(r);
  const prx=v[0]*cr-v[1]*sr, pry=v[0]*sr+v[1]*cr, prz=v[2];
  const ppx=prx, ppy=pry*cp-prz*sp, ppz=pry*sp+prz*cp;
  return [ppx*ch+ppz*sh, ppy, -ppx*sh+ppz*ch];
}
function basis(truck, head, cabin, negRoll){
  const F=[0,0,-1],R=[1,0,0],U=[0,1,0];
  const chain=[head,cabin,truck];
  const out={};
  [['f',F],['r',R],['u',U]].forEach(([k,v])=>{
    let x=v.slice();
    chain.forEach(o=>{ x=rot(x,o,negRoll); });
    const L=Math.hypot(...x); out[k]=x.map(a=>a/L);
  });
  return out;
}
const truck={h:0.6514,p:-1.71/360,r:-6.81/360};
const head ={h:0,      p: 1.41/360,r:0};
const cab  ={h:0,      p: 0,       r:-0.11/360};
const a=basis(truck,head,cab,false);
const b=basis(truck,head,cab,true);
const camRoll=(B)=>Math.asin(Math.max(-1,Math.min(1,-B.r[1]/Math.sqrt(Math.max(1e-9,1-B.f[1]*B.f[1])))))*180/Math.PI;
const camNose=(B)=>Math.asin(Math.max(-1,Math.min(1,-B.f[1])))*180/Math.PI;
console.log("CURRENT  fwd.Y=%s right.Y=%s up.Y=%s | camRoll=%s nose=%s",
  a.f[1].toFixed(4),a.r[1].toFixed(4),a.u[1].toFixed(4),camRoll(a).toFixed(2),camNose(a).toFixed(2));
console.log("NEGROLL  fwd.Y=%s right.Y=%s up.Y=%s | camRoll=%s nose=%s",
  b.f[1].toFixed(4),b.r[1].toFixed(4),b.u[1].toFixed(4),camRoll(b).toFixed(2),camNose(b).toFixed(2));
console.log("LOGGED   fwd.Y=0.0409 right.Y=-0.1229 up.Y=0.9916 | camRoll=7.07 nose=-2.35  truckRoll=-6.81");
