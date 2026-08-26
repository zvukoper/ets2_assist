// ================================================================
// ПАНЕЛЬ PITCH/ROLL (экстремумы и обновление)
// ================================================================
let pitchRollState = { pitchMax: -Infinity, pitchMin: Infinity, rollMax: -Infinity, rollMin: Infinity };
function updatePitchRollPanel(pitchDeg, rollDeg) {
    if (pitchDeg > pitchRollState.pitchMax) pitchRollState.pitchMax = pitchDeg;
    if (pitchDeg < pitchRollState.pitchMin) pitchRollState.pitchMin = pitchDeg;
    if (rollDeg > pitchRollState.rollMax) pitchRollState.rollMax = rollDeg;
    if (rollDeg < pitchRollState.rollMin) pitchRollState.rollMin = rollDeg;
    document.getElementById('pitchValue3d').textContent = pitchDeg.toFixed(1) + '°';
    document.getElementById('rollValue3d').textContent = rollDeg.toFixed(1) + '°';
    document.getElementById('pitchMin3d').textContent = pitchRollState.pitchMin.toFixed(1);
    document.getElementById('pitchMax3d').textContent = pitchRollState.pitchMax.toFixed(1);
    document.getElementById('rollMin3d').textContent = pitchRollState.rollMin.toFixed(1);
    document.getElementById('rollMax3d').textContent = pitchRollState.rollMax.toFixed(1);
    const resetBtn = document.getElementById('resetPitchRollBtn');
    if (resetBtn) {
        resetBtn.onclick = function() {
            pitchRollState.pitchMax = -Infinity;
            pitchRollState.pitchMin = Infinity;
            pitchRollState.rollMax = -Infinity;
            pitchRollState.rollMin = Infinity;
            if (window.currentPitchRoll) {
                updatePitchRollPanel(window.currentPitchRoll.pitch, window.currentPitchRoll.roll);
            }
        };
    }
}