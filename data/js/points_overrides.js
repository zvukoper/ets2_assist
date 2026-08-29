// ================================================================
// OVERRIDES РЕДАКТОРА НА МИНИКАРТЕ
// Приложение шлёт points_overrides_data: ТОЛЬКО переопределённые
// города/POI (delta поверх статики) и пользовательские точки.
// Статические базы (cities.geojson / Overlay.json) страница грузит
// сама — здесь мы лишь накладываем delta при отрисовке (effective*).
// state.cities / state.pois НЕ мутируются.
// ================================================================

// null = данные ещё не приходили (рисуем чистую статику).
let _pointsOverrides = null;
let _pointsOverridesUserPoints = [];

function storePointsOverrides(data) {
	try {
		_pointsOverrides = {
			cities: Array.isArray(data && data.cities) ? data.cities : [],
			pois: Array.isArray(data && data.pois) ? data.pois : []
		};
		_pointsOverridesUserPoints = Array.isArray(data && data.userPoints) ? data.userPoints : [];
		console.log(`[OVR] points_overrides_data: cities=${_pointsOverrides.cities.length}, pois=${_pointsOverrides.pois.length}, userPoints=${_pointsOverridesUserPoints.length}`);
		if (typeof drawMinimap === 'function') drawMinimap();
	} catch (e) { console.warn('[OVR] storePointsOverrides:', e); }
}

// Эффективный список городов: статика + delta-merge по gameName + фильтр hidden.
function getEffectiveCityList() {
	if (!_pointsOverrides || !_pointsOverrides.cities.length) return state.cities;
	const ovr = new Map();
	for (const o of _pointsOverrides.cities) ovr.set(o.gameName, o);
	const merged = state.cities.map(c => {
		const o = ovr.get(c.gameName);
		if (!o) return c;
		return { x: Number(o.x), z: Number(o.z), name: o.realName || c.name, gameName: c.gameName, hidden: !!o.hidden };
	});
	// Город из payload, отсутствующий в статике страницы (напр. другой map-mod).
	const known = new Set(state.cities.map(c => c.gameName));
	for (const o of _pointsOverrides.cities) {
		if (!known.has(o.gameName)) {
			if (!o.hidden) merged.push({ x: Number(o.x), z: Number(o.z), name: o.realName || o.gameName, gameName: o.gameName });
			known.add(o.gameName);
		}
	}
	return merged.filter(c => !c.hidden);
}

// Эффективный список POI: статика + delta-merge по uid + пользовательские точки
// (type 'custom') + фильтр hidden.
function getEffectivePoiList() {
	const users = _pointsOverridesUserPoints
		.filter(u => !u.hidden)
		.map(u => ({ x: Number(u.x), z: Number(u.z), type: 'custom', name: u.realName || u.gameName, uid: u.gameName, color: u.color }));
	if (!_pointsOverrides || !_pointsOverrides.pois.length) return state.pois.concat(users);
	const ovr = new Map();
	for (const o of _pointsOverrides.pois) ovr.set(o.uid, o);
	const merged = state.pois.map(p => {
		if (!p.uid) return p;
		const o = ovr.get(p.uid);
		if (!o) return p;
		return { x: Number(o.x), z: Number(o.z), type: o.category || p.type, name: o.name || p.name, uid: p.uid, hidden: !!o.hidden };
	});
	const known = new Set(state.pois.map(p => p.uid).filter(Boolean));
	for (const o of _pointsOverrides.pois) {
		if (!o.uid || known.has(o.uid)) continue;
		if (!o.hidden) merged.push({ x: Number(o.x), z: Number(o.z), type: o.category || 'custom', name: o.name || o.uid, uid: o.uid });
		known.add(o.uid);
	}
	return merged.filter(p => !p.hidden).concat(users);
}