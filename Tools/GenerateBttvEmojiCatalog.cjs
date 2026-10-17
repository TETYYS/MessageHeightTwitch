// Regenerates BTTVSupportedEmojis.json from the dependencies used by BetterTTV.
// Usage: node Tools/GenerateBttvEmojiCatalog.cjs <emoji-toolkit/emoji.json> <@twemoji/api/dist/twemoji.cjs.js> <betterttv/src/utils/emoji-blacklist.js>
// Match BetterTTV's vite.config.js jsonTransform and src/modules/emotes/emojis.js loadEmojis.
// The checked-in catalog uses BetterTTV commit 5e7fe4cda2981ed698d0725a83ca278ec8b3dfbf,
// emoji-toolkit 11.0.0, and @twemoji/api 17.0.3.
const fs = require('node:fs');
const path = require('node:path');

const [emojiPath, twemojiPath, blacklistPath] = process.argv.slice(2);
if (!emojiPath || !twemojiPath || !blacklistPath) {
  throw new Error('Expected emoji-toolkit emoji.json, @twemoji/api CommonJS module, and BetterTTV emoji blacklist paths.');
}

const source = JSON.parse(fs.readFileSync(emojiPath, 'utf8'));
const twemoji = require(path.resolve(twemojiPath));
const blacklistSource = fs.readFileSync(blacklistPath, 'utf8');
if (!/^\s*export default\s*\[[\s\S]*\];\s*$/.test(blacklistSource)) {
  throw new Error('Unexpected BetterTTV blacklist format.');
}
const blacklist = new Set([...blacklistSource.matchAll(/'([^']*)'/g)].map((match) => match[1]));

const bySlug = {};
for (const emojiData of Object.values(source)) {
  const char = emojiData.code_points.fully_qualified
    .split('-')
    .map((codePoint) => String.fromCodePoint(parseInt(codePoint, 16)))
    .join('');
  const emoji = {char, slug: emojiData.shortname.replace(/:/g, '')};
  bySlug[emoji.slug] = emoji;
  for (const alternative of emojiData.shortname_alternates) {
    if (alternative !== ':tf:') {
      bySlug[alternative.replace(/:/g, '')] = {...emoji, isAlternative: true};
    }
  }
}

const supported = new Set();
for (const emoji of Object.values(bySlug)) {
  if (blacklist.has(emoji.char)) continue;

  let codePointCount = 0;
  twemoji.parse(emoji.char, (icon) => { codePointCount += icon.split('-').length; });
  if (codePointCount !== 1) continue;

  let hasImage = false;
  twemoji.parse(emoji.char, {
    callback: (icon) => {
      if (icon && !['a9', 'ae', '2122'].includes(icon)) hasImage = true;
      return false;
    },
  });
  if (hasImage) supported.add(emoji.char);
}

const output = path.resolve(__dirname, '..', 'BTTVSupportedEmojis.json');
fs.writeFileSync(output, JSON.stringify([...supported], null, 2) + '\n');
console.log(`Wrote ${supported.size} BetterTTV emoji to ${output}`);
