import { readFile, readdir, realpath, mkdir, writeFile } from 'node:fs/promises';
import { dirname, isAbsolute, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const projectRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const editorcnVersion = '0.3.4';
const editorcnSource = 'https://github.com/shadcn-labs/editorcn/blob/2b15db504a13140f875a99ac276b9bb1f0e4ce45/LICENSE';
const editorcnLicense = resolve(projectRoot, 'packaging/licenses/editorcn-0.3.4-MIT.txt');
const noticeFile = /^(?:licen[cs]e|copying|copyright|notice)(?:[.-].*)?$/i;

async function readJson(path) {
  try { return JSON.parse(await readFile(path, 'utf8')); }
  catch (error) {
    if (error.code === 'ENOENT') return null;
    throw error;
  }
}

async function packageForInput(input, cache) {
  const resolved = await realpath(isAbsolute(input) ? input : resolve(projectRoot, input));
  let directory = dirname(resolved);
  const visited = [];
  while (directory !== dirname(directory)) {
    if (cache.has(directory)) {
      const found = cache.get(directory);
      for (const path of visited) cache.set(path, found);
      return found;
    }
    visited.push(directory);
    const manifest = await readJson(join(directory, 'package.json'));
    if (manifest?.name && manifest.version) {
      const found = { directory, manifest };
      for (const path of visited) cache.set(path, found);
      return found;
    }
    directory = dirname(directory);
  }
  throw new Error(`Cannot find the package manifest for bundled input ${input}.`);
}

async function licenseForPackage({ directory, manifest }) {
  const names = (await readdir(directory, { withFileTypes: true }))
    .filter(entry => entry.isFile() && noticeFile.test(entry.name))
    .map(entry => entry.name).sort((a, b) => a.localeCompare(b, 'en'));
  const documents = await Promise.all(names.map(async name => ({
    name, text: (await readFile(join(directory, name), 'utf8')).replace(/\r\n/g, '\n').trim(),
  })));
  if (!documents.some(item => /^(?:licen[cs]e|copying)(?:[.-].*)?$/i.test(item.name) && item.text)) {
    // The 0.3.4 npm tarball omits LICENSE. Its npm gitHead points to this
    // upstream revision; keep its exact MIT license locally for offline builds.
    if (manifest.name === '@editorcn/block-editor' && manifest.version === editorcnVersion) {
      documents.unshift({ name: `LICENSE (upstream: ${editorcnSource})`, text: (await readFile(editorcnLicense, 'utf8')).trim() });
    } else {
      throw new Error(`Missing full license text for bundled package ${manifest.name}@${manifest.version}. Review the pinned upstream license before shipping.`);
    }
  }
  const license = manifest.name === '@editorcn/block-editor' && manifest.version === editorcnVersion
    ? 'MIT' : typeof manifest.license === 'string' ? manifest.license : manifest.license?.type;
  if (!license) throw new Error(`Missing license identifier for bundled package ${manifest.name}@${manifest.version}.`);
  const repository = typeof manifest.repository === 'string' ? manifest.repository : manifest.repository?.url;
  return { name: manifest.name, version: manifest.version, license, repository: repository || manifest.homepage || '', documents };
}

/** Build-time notices for packages that contribute bytes to the emitted editor. */
export async function writeEditorLicenses(metafile, outdir) {
  if (!metafile?.outputs) throw new Error('Editor license collection requires the esbuild metafile.');
  const outputDirectory = resolve(outdir);
  const relativeOutput = relative(projectRoot, outputDirectory);
  if (relativeOutput === '..' || relativeOutput.startsWith(`..${sep}`) || isAbsolute(relativeOutput)) {
    throw new Error('Editor notices must be written inside the Jot workspace.');
  }
  const bundledInputs = new Set();
  for (const output of Object.values(metafile.outputs)) {
    for (const [input, contribution] of Object.entries(output.inputs || {})) {
      if (contribution.bytesInOutput > 0 && /(?:^|[\\/])node_modules[\\/]/.test(input)) bundledInputs.add(input);
    }
  }
  if (!bundledInputs.size) throw new Error('No third-party editor inputs were found; do not ship an empty notice file.');
  const packageCache = new Map();
  const packageRoots = new Map();
  for (const input of [...bundledInputs].sort()) {
    const found = await packageForInput(input, packageCache);
    packageRoots.set(found.directory, found);
  }
  const packages = new Map();
  for (const found of packageRoots.values()) {
    const item = await licenseForPackage(found);
    const key = `${item.name}@${item.version}`;
    const previous = packages.get(key);
    if (previous && JSON.stringify(previous) !== JSON.stringify(item)) {
      throw new Error(`Conflicting license information for ${key}. Review both installed copies.`);
    }
    packages.set(key, item);
  }
  const ordered = [...packages.values()].sort((a, b) => `${a.name}@${a.version}`.localeCompare(`${b.name}@${b.version}`, 'en'));
  const sections = ordered.map(item => [
    '='.repeat(78), `${item.name}@${item.version}`, `License: ${item.license}`,
    ...(item.repository ? [`Source: ${item.repository}`] : []), '',
    ...item.documents.flatMap(document => [`--- ${document.name} ---`, document.text, '']),
  ].join('\n'));
  const text = [
    'Jot rich-text editor — third-party notices',
    '',
    'Generated from the files included in this editor build. These notices cover',
    'the JavaScript/CSS editor bundle, not the separate Windows/.NET components.',
    'Build dependencies that do not contribute code to the bundle are omitted.',
    '',
    ...ordered.map(item => `${item.name}@${item.version} — ${item.license}`),
    '', ...sections,
  ].join('\n').trimEnd() + '\n';
  await mkdir(outputDirectory, { recursive: true });
  const path = join(outputDirectory, 'editor-licenses.txt');
  await writeFile(path, text, 'utf8');
  return { path, packages: ordered.map(({ name, version, license }) => ({ name, version, license })) };
}
