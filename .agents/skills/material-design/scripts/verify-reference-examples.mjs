// Run with: node scripts/verify-reference-examples.mjs <dependency-workspace> [skill-root]
// The workspace must supply @material/material-color-utilities, @angular/material,
// @angular/cdk, and sass. Install them outside the skill; this script installs nothing.
// Select versions that load in the target Node runtime and record those versions.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

if (!process.argv[2]) {
  console.error('Usage: node verify-reference-examples.mjs <dependency-workspace> [skill-root]');
  process.exit(2);
}
const workspace = path.resolve(process.argv[2]);
const skillRoot = process.argv[3]
  ? path.resolve(process.argv[3])
  : path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const requireFromWorkspace = createRequire(path.join(workspace, 'package.json'));
const moduleUrl = name => pathToFileURL(requireFromWorkspace.resolve(name)).href;

function example(relativePath, language, marker) {
  const text = readFileSync(path.join(skillRoot, relativePath), 'utf8');
  const blocks = [...text.matchAll(new RegExp('```' + language + '\\r?\\n([\\s\\S]*?)```', 'g'))];
  const matches = blocks.map(match => match[1]).filter(code => code.includes(marker));
  assert.equal(matches.length, 1, `Expected one ${marker} example in ${relativePath}`);
  return matches[0];
}

let failures = 0;
async function check(name, run) {
  try {
    await run();
    console.log(`PASS: ${name}`);
  } catch (error) {
    failures++;
    console.error(`FAIL: ${name}: ${error.message}`);
  }
}

await check('JavaScript tonal palette produces requested tones', async () => {
  const colorUtilitiesUrl = moduleUrl('@material/material-color-utilities');
  const code = example('references/color-system.md', 'javascript', 'const primaryPalette');
  const runnable = code.replace(
    /(['"])@material\/material-color-utilities\1/,
    JSON.stringify(colorUtilitiesUrl),
  );
  const exported = await import('data:text/javascript;base64,' + Buffer.from(
    runnable + '\nexport { primaryPalette };',
  ).toString('base64'));
  const { Hct } = await import(colorUtilitiesUrl);
  for (const tone of [0, 40, 55, 90, 100]) {
    const argb = exported.primaryPalette.tone(tone);
    assert.ok(Number.isInteger(argb));
    assert.equal((argb >>> 24) & 255, 255, 'Expected opaque ARGB');
    assert.ok(Math.abs(Hct.fromInt(argb).tone - tone) < 1, `Incorrect tone ${tone}`);
  }
});

for (const relativePath of ['references/platform-guides.md', 'references/material-2.md']) {
  await check(`Angular M2 Sass compiles: ${relativePath}`, async () => {
    const sass = requireFromWorkspace('sass');
    const code = example(relativePath, 'scss', '$m2-primary:');
    const warnings = new Set();
    const result = sass.compileString(code, {
      loadPaths: [path.join(workspace, 'node_modules')],
      logger: { warn: message => warnings.add(message) },
    });
    assert.ok(result.css.length > 1000, 'Expected generated component theme CSS');
    console.log(`  Generated ${result.css.length} CSS characters; ${warnings.size} unique Sass warnings`);
  });
}
console.log(`${3 - failures}/3 reference examples passed`);
process.exitCode = failures ? 1 : 0;
