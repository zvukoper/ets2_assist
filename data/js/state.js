// ================================================================
// СОСТОЯНИЕ
// ================================================================
const state = {
    truck: { x:0, y:0, z:0, heading:0, pitch:0, roll:0 },
    target: { x:0, y:0, z:0 },
    customTargets: [],
    targetMapOverview: false,
    distance: 0,
    relativeAngle: 0,
    absoluteAngle: 0,
    cities: [],
    roads: [],
    pois: [],
    poiCategories: [],
    poiCategoryCounts: {},
    manualScaleFactor: 1,
    autoScale: 1,
    scale: 1,
    currentScale: 1,
    targetActive: false,
    rawHeading: 0,
    trail: [],
    dataPoints: [],
    eventMarkers: [],
    isTruckInWorld: false,
    speed: 0,
    wsDataReceived: false,
    nearbyCities: [],
    step: 200,
    zoomOnMapTargets: [],
    _lastSpeed: 0,
    _speedHoldCount: 0,
    engineOn: false,
    fuel: 0,
    damage: 0,
    cargoDamage: 0,
    trailerAttached: false,
    lastDamage: 0,
    damageAccumulator: 0,
    damageTimer: null,
    damageStartPos: null,
    parkingStartGameTime: null,
    parkingStartRealTime: null,
    parkingStartPos: null,
    isParking: false,
    isStopped: false,
    stopStartRealTime: null,
    stopPos: null,
    stopTextMarker: null,
    totalRealDistance: 0,
    lastDistanceMarker: 0,
    distanceMarkerThreshold: 2000,
    firstMovementDetected: false,
    trailStartTime: null,
    prevPos: null,
    prevSpeed: 0,
    gameTime: null,
    jobDestination: '',
    estimatedDistance: 0,
    _lastLoggedSpeed: 0,
    _lastDataDist: 0,
    elapsedSeconds: 0,
    measureMode: false,
    measureStart: null,
    notes: [],
    // Экстремумы pitch/roll
    _pitchMax: -Infinity,
    _pitchMin: Infinity,
    _rollMax: -Infinity,
    _rollMin: Infinity,
    // Новые поля
    lights: { aux: 0, beacon: false, beamHigh: false, beamLow: false, brake: false, leftBlinker: false, rightBlinker: false, parking: false, reverse: false },
    gameTimeMinutes: 0,
    localScale: 1.0,
    steering: 0,
    throttle: 0,
    brake: 0,
    odometer: 0,
    headOffset: [0,0,0,0,0,0],
    // v73: пометка «Пометить в АР» на миникарте ({x,y,z} | null) — ar_pin_map.
    arPinMap: null
};

// Дополнительные переменные для случайной цели
let randomTarget = null;
let randomTargetReachedSent = false;
// Массив ВСЕХ активных случайных целей (поддержка нескольких одновременно).
// Каждая: { id, x, y, z, name, color, icon, isRandom, radius, inZone, armed }
state.randomTargets = [];

const TRAIL_LENGTH = -1;

// v74: дороги толще В 2.5 РАЗА (фидбек 31.08.2026). Минимум стал 8.75 px
// (v73 был равен шлейфу 3.5 — теперь заметно толще).
const MIN_ROAD_WIDTH = 8.75;
const roadWidthStyles = {
    'balt11': 17.5, 'balt7': 14.0, 'balt35': 14.0,
    'ger13': 15.0, 'ger16': 15.0,
    'un11_sw': 10.5, 'un11': 10.5,
    'un7': 8.75, 'un7_sw': 8.75,
    'road': 10.0, 'default': 8.75,
};

const EVENT_TYPES = {
    'stop': 1,
    'service': 2,
    'parking': 3,
    'damage': 4,
    'start': 5,
    'distance': 6,
    'save': 7,
    'info': 8,
};
const EVENT_TYPE_NAMES = Object.fromEntries(Object.entries(EVENT_TYPES).map(([k,v]) => [v,k]));