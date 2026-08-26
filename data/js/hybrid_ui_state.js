// ================================================================
// СОСТОЯНИЕ HYBRID UI
// ================================================================
const hybridState = {
    // Данные из HTTP
    fuelPercent: 0,
    restHours: 0,
    jobRemaining: 0,
    hasJob: false,
    jobInitial: 0,
    estimatedHours: 0,
    estimatedDistance: 0,
    initialDistance: 0,
    parking: 'OFF',
    engine: 'OFF',
    rangeMin: 400,
    rangeMax: 623,
    trailerAttached: false,
    lights: 'off',
    brake: 0,
    brakeLight: false,
    arduino: false,
    _restFromTruckTel: false,
    _parkingFromTruckTel: false,
    _trailerFromTruckTel: false,
    // Сохранённые начальные значения
    _initialDistance: null,
    _initialJobRemaining: null,
    // WebSocket
    wsConnected: false,
    lastSpeed: 0,
    speedFromHttp: 0,
    // UI
    dataShown: false,
    speedZoneState: null,
    lastSpeedPrev: 0,
    // Язык
    lang: {},
    wsPort: 8080,
    wsUrl: null,
    socket: null
};