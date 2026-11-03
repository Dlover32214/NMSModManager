import fs from "node:fs";
import path from "node:path";
import {fileURLToPath} from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const projectRoot = path.resolve(scriptDirectory, "..");
const lrcPath = path.join(projectRoot, "public", "lost-and-found.lrc");
const outputPath = path.join(projectRoot, "src", "lyrics.json");
const source = fs.readFileSync(lrcPath, "utf8");

const englishEntries = [];
const chineseEntries = [];

for (const rawLine of source.split(/\r?\n/)) {
  if (!rawLine.trim()) continue;

  const timestamps = [...rawLine.matchAll(/\[(\d{2}):(\d{2})\.(\d{2,3})\]/g)];
  if (timestamps.length === 0) continue;

  const text = rawLine.replace(/\[[^\]]+\]/g, "").trim();
  if (!text) continue;
  if (/^Lost & Found - Feint/i.test(text)) continue;

  for (const timestamp of timestamps) {
    const minutes = Number(timestamp[1]);
    const seconds = Number(timestamp[2]);
    const fraction = timestamp[3].padEnd(3, "0");
    const startMs = minutes * 60_000 + seconds * 1_000 + Number(fraction);
    if (/[A-Za-z]/.test(text)) {
      englishEntries.push({text, startMs});
    } else if (/[\u3400-\u9fff]/.test(text)) {
      chineseEntries.push({text, startMs});
    }
  }
}

const english = [...new Map(englishEntries.map((entry) => [`${entry.startMs}:${entry.text}`, entry])).values()]
  .filter((entry) => entry.startMs < 110_000)
  .sort((left, right) => left.startMs - right.startMs);
const chinese = [...new Map(chineseEntries.map((entry) => [`${entry.startMs}:${entry.text}`, entry])).values()]
  .filter((entry) => entry.startMs < 110_000)
  .sort((left, right) => left.startMs - right.startMs);

const captions = english.map((entry, index) => {
  const nextStart = english[index + 1]?.startMs ?? entry.startMs + 6_000;
  const endMs = Math.min(nextStart, entry.startMs + 6_500);
  const translation = chinese[index]?.text ?? "";

  return {
    text: ` ${entry.text}\n${translation}`,
    startMs: entry.startMs,
    endMs: Math.max(entry.startMs + 600, endMs),
    timestampMs: entry.startMs,
    confidence: null,
    pageBreakAfter: true,
  };
});

fs.writeFileSync(outputPath, `${JSON.stringify(captions, null, 2)}\n`, "utf8");
console.log(`Wrote ${captions.length} lyric captions to ${outputPath}`);
