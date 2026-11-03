import fs from "node:fs";
import path from "node:path";
import {fileURLToPath} from "node:url";

const sampleRate = 44100;
const durationSeconds = 42;
const sampleCount = sampleRate * durationSeconds;
const samples = new Float64Array(sampleCount);

const chords = [
  [130.81, 155.56, 196.0],
  [116.54, 146.83, 174.61],
  [155.56, 174.61, 207.65],
  [146.83, 174.61, 220.0],
  [130.81, 155.56, 196.0],
  [116.54, 146.83, 174.61],
  [103.83, 130.81, 155.56],
  [146.83, 174.61, 220.0],
  [130.81, 155.56, 196.0],
  [116.54, 146.83, 174.61],
];

const chordLength = 4.05;
let randomState = 0x12345678;

const random = () => {
  randomState = (1664525 * randomState + 1013904223) >>> 0;
  return randomState / 0xffffffff;
};

const addSample = (index, value) => {
  if (index >= 0 && index < sampleCount) {
    samples[index] += value;
  }
};

const smoothStep = (edge0, edge1, value) => {
  const x = Math.max(0, Math.min(1, (value - edge0) / (edge1 - edge0)));
  return x * x * (3 - 2 * x);
};

for (let chordIndex = 0; chordIndex < chords.length; chordIndex++) {
  const chord = chords[chordIndex];
  const start = chordIndex * chordLength;
  const end = Math.min(durationSeconds, start + chordLength + 0.8);

  for (const [noteIndex, frequency] of chord.entries()) {
    for (let sample = Math.floor(start * sampleRate); sample < end * sampleRate; sample++) {
      const time = sample / sampleRate;
      const local = time - start;
      const envelope = smoothStep(0, 1.1, local) * (1 - smoothStep(chordLength - 0.15, end - start, local));
      const drift = Math.sin(time * 0.31 + noteIndex * 1.7) * 0.8;
      const pad =
        Math.sin(2 * Math.PI * (frequency + drift) * time) * 0.018 +
        Math.sin(2 * Math.PI * frequency * 2.002 * time) * 0.006;
      addSample(sample, pad * envelope);
    }
  }

  const step = 60 / 100 / 2;
  const arpPattern = [0, 2, 1, 2, 0, 1, 2, 1];
  for (let note = 0; note < 16; note++) {
    const noteStart = start + note * step;
    if (noteStart >= durationSeconds) continue;
    const chordTone = chord[arpPattern[note % arpPattern.length]];
    const frequency = chordTone * (note % 8 >= 4 ? 4 : 2);
    const noteLength = 0.58;
    for (let sample = Math.floor(noteStart * sampleRate); sample < (noteStart + noteLength) * sampleRate; sample++) {
      const local = sample / sampleRate - noteStart;
      const envelope = Math.exp(-local * 7.2) * smoothStep(0, 0.018, local);
      const tone =
        Math.sin(2 * Math.PI * frequency * local) * 0.035 +
        Math.sin(2 * Math.PI * frequency * 2 * local) * 0.009;
      addSample(sample, tone * envelope);
    }
  }

  for (let beat = 0; beat < 7; beat++) {
    const beatStart = start + beat * (60 / 100);
    if (beatStart >= durationSeconds) continue;
    for (let sample = Math.floor(beatStart * sampleRate); sample < (beatStart + 0.22) * sampleRate; sample++) {
      const local = sample / sampleRate - beatStart;
      const envelope = Math.exp(-local * 22);
      const pitch = 78 - local * 150;
      addSample(sample, Math.sin(2 * Math.PI * pitch * local) * envelope * 0.06);
    }
  }
}

for (let time = 0; time < durationSeconds; time += 1.6) {
  const sparkleStart = time + 0.29;
  const frequency = 880 * (1 + Math.floor(random() * 4) * 0.25);
  for (let sample = Math.floor(sparkleStart * sampleRate); sample < (sparkleStart + 0.7) * sampleRate; sample++) {
    const local = sample / sampleRate - sparkleStart;
    const envelope = Math.exp(-local * 6.4) * smoothStep(0, 0.025, local);
    addSample(sample, Math.sin(2 * Math.PI * frequency * local) * envelope * 0.011);
  }
}

for (let sample = 0; sample < sampleCount; sample++) {
  const time = sample / sampleRate;
  const masterFade = smoothStep(0, 1.5, time) * (1 - smoothStep(durationSeconds - 2.3, durationSeconds, time));
  samples[sample] = Math.tanh(samples[sample] * 1.2) * masterFade;
}

let peak = 0;
for (const value of samples) {
  peak = Math.max(peak, Math.abs(value));
}

const gain = peak > 0 ? 0.72 / peak : 1;
const dataSize = sampleCount * 2;
const buffer = Buffer.alloc(44 + dataSize);
buffer.write("RIFF", 0);
buffer.writeUInt32LE(36 + dataSize, 4);
buffer.write("WAVE", 8);
buffer.write("fmt ", 12);
buffer.writeUInt32LE(16, 16);
buffer.writeUInt16LE(1, 20);
buffer.writeUInt16LE(1, 22);
buffer.writeUInt32LE(sampleRate, 24);
buffer.writeUInt32LE(sampleRate * 2, 28);
buffer.writeUInt16LE(2, 32);
buffer.writeUInt16LE(16, 34);
buffer.write("data", 36);
buffer.writeUInt32LE(dataSize, 40);

for (let sample = 0; sample < sampleCount; sample++) {
  const value = Math.max(-1, Math.min(1, samples[sample] * gain));
  buffer.writeInt16LE(Math.round(value * 32767), 44 + sample * 2);
}

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const outputPath = path.resolve(scriptDirectory, "../public/soundtrack.wav");
fs.mkdirSync(path.dirname(outputPath), {recursive: true});
fs.writeFileSync(outputPath, buffer);
console.log(`Wrote ${outputPath}`);
