// Pinned, offline-at-runtime emoji assets. Run with --check for local verification.
// Source assets remain byte-for-byte unchanged and carry their upstream licenses.
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import vm from 'node:vm';

const folder=new URL('../assets/emoji/',import.meta.url);
const artworkFolder=new URL('artwork/',folder);
const sources={
  twemoji:{repository:'jdecked/twemoji',commit:'b6b55fef1e8636b540a6d016a4729ca8cdf2e60b',version:'17.0.3',license:'CC-BY-4.0'},
  noto:{repository:'googlefonts/noto-emoji',commit:'e20cbc2bbec1926686be9f9bee7d1d2cfa1fea0e',version:'Unicode 18 additions',license:'Apache-2.0'},
};
const check=process.argv.includes('--check');
const context={window:{}};
vm.runInNewContext(await readFile(new URL('emoji-data.js',folder),'utf8'),context);
const catalogue=context.window.JotEmojiData;
const key=emoji=>[...emoji].filter(char=>char.codePointAt(0)!==0xfe0f).map(char=>char.codePointAt(0).toString(16)).join('-');
const sha256=bytes=>createHash('sha256').update(bytes).digest('hex');
const gitHash=bytes=>createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex');
async function download(url){
  let last;
  for(let retry=0;retry<4;retry++){
    try{const response=await fetch(url,{signal:AbortSignal.timeout(45000)});if(!response.ok)throw new Error(`${response.status} ${url}`);return Buffer.from(await response.arrayBuffer());}
    catch(error){last=error;if(retry<3)await new Promise(resolve=>setTimeout(resolve,(retry+1)*800));}
  }
  throw last;
}
function assertSafeSvg(bytes,path){
  const text=bytes.toString('utf8');
  // SVGs are loaded only as image resources, never inserted as document markup.
  if(!/<svg[\s>]/i.test(text)||/<script\b|<foreignObject\b|\son[a-z]+\s*=|(?:href|src)\s*=\s*["'](?:https?:|\/\/|javascript:)/i.test(text))throw new Error(`Unsafe SVG: ${path}`);
}
const generated=new URL('emoji-artwork.js',folder);
const manifestFile=new URL('artwork-manifest.json',folder);
if(check){
  const manifest=JSON.parse(await readFile(manifestFile,'utf8'));
  vm.runInNewContext(await readFile(generated,'utf8'),context);
  const mapping=context.window.JotEmojiArtwork.files;
  for(const emoji of catalogue.aliases)if(mapping[key(emoji)]!==key(emoji)+'.svg')throw new Error(`Missing or incorrect artwork: ${emoji}`);
  if(manifest.files.length!==catalogue.rows.length||Object.keys(mapping).length!==catalogue.rows.length)throw new Error('Artwork count differs from catalogue.');
  for(const file of manifest.files){const bytes=await readFile(new URL(file.file,artworkFolder));assertSafeSvg(bytes,file.file);if(sha256(bytes)!==file.sha256)throw new Error(`Changed artwork: ${file.file}`);}
  for(const file of manifest.licenses)if(sha256(await readFile(new URL(file.file,folder)))!==file.sha256)throw new Error(`Changed license: ${file.file}`);
  console.log(JSON.stringify({checked:true,catalogue:catalogue.version,fullyQualified:catalogue.rows.length,aliases:catalogue.aliases.length,artworks:manifest.files.length,missing:0}));
  process.exit(0);
}
await mkdir(artworkFolder,{recursive:true});
const trees=Object.fromEntries(await Promise.all(Object.entries(sources).map(async([name,source])=>{
  const tree=JSON.parse((await download(`https://api.github.com/repos/${source.repository}/git/trees/${source.commit}?recursive=1`)).toString('utf8'));
  if(tree.truncated)throw new Error(`Truncated source inventory: ${name}`);
  return [name,new Map(tree.tree.filter(item=>item.type==='blob').map(item=>[item.path,item]))];
})));
const selection=new Map(),mapping={};
for(const [emoji,name] of catalogue.rows){
  const normalized=key(emoji),raw=[...emoji].map(char=>char.codePointAt(0).toString(16)).join('-');
  const twemojiPath=[`assets/svg/${raw}.svg`,`assets/svg/${normalized}.svg`].find(path=>trees.twemoji.has(path));
  const source=twemojiPath?'twemoji':'noto',path=twemojiPath||`2D/svg/emoji_u${normalized.split('-').map(point=>point.padStart(4,'0')).join('_')}.svg`;
  if(!trees[source].has(path))throw new Error(`No approved artwork for ${name} (${normalized}).`);
  const file=`${normalized}.svg`;mapping[normalized]=file;selection.set(file,{file,source,path,gitSha:trees[source].get(path).sha});
}
for(const emoji of catalogue.aliases)if(!mapping[key(emoji)])throw new Error(`Unmapped accepted alias: ${emoji}`);
const files=[...selection.values()];let next=0,complete=0;
await Promise.all(Array.from({length:20},async()=>{
  for(;;){const index=next++;if(index>=files.length)return;const file=files[index],source=sources[file.source],target=new URL(file.file,artworkFolder);let bytes;
    try{bytes=await readFile(target);if(gitHash(bytes)!==file.gitSha)bytes=null;}catch(error){if(error.code!=='ENOENT')throw error;}
    if(!bytes){bytes=await download(`https://raw.githubusercontent.com/${source.repository}/${source.commit}/${file.path}`);if(gitHash(bytes)!==file.gitSha)throw new Error(`Upstream integrity mismatch: ${file.path}`);assertSafeSvg(bytes,file.path);await writeFile(target,bytes);}
    file.sha256=sha256(bytes);file.bytes=bytes.length;if(++complete%500===0)console.log(`Emoji artwork ${complete}/${files.length}`);
  }
}));
const licenseRequests=[
  ['TWEMOJI-LICENSE-GRAPHICS.txt',`https://raw.githubusercontent.com/${sources.twemoji.repository}/${sources.twemoji.commit}/LICENSE-GRAPHICS`],
  ['NOTO-SVG-NOTICE.txt',`https://raw.githubusercontent.com/${sources.noto.repository}/${sources.noto.commit}/2D/svg/LICENSE`],
  ['APACHE-2.0.txt','https://www.apache.org/licenses/LICENSE-2.0.txt'],
];
const licenses=[];
for(const [file,url]of licenseRequests){const bytes=await download(url);await writeFile(new URL(file,folder),bytes);licenses.push({file,url,sha256:sha256(bytes)});}
const manifest={catalogueVersion:catalogue.version,sources,fullyQualified:catalogue.rows.length,acceptedAliases:catalogue.aliases.length,licenses,files};
await writeFile(manifestFile,JSON.stringify(manifest,null,2)+'\n');
await writeFile(generated,`// Generated by scripts/generate-emoji-artwork.mjs; see ARTWORK-NOTICE.txt.\n'use strict';\nwindow.JotEmojiArtwork=${JSON.stringify({version:catalogue.version,files:mapping})};\n`);
await writeFile(new URL('ARTWORK-NOTICE.txt',folder),`Jot local emoji artwork\n\nTwemoji v${sources.twemoji.version}, copyright Twitter, Inc. and other contributors, licensed CC BY 4.0.\nhttps://github.com/${sources.twemoji.repository}/tree/${sources.twemoji.commit}\nLicense: TWEMOJI-LICENSE-GRAPHICS.txt\n\nUnicode 18 additions: Noto Emoji, copyright 2013 Google, Inc. All Rights Reserved.\nhttps://github.com/${sources.noto.repository}/tree/${sources.noto.commit}/2D/svg\nLicensed Apache 2.0; see NOTO-SVG-NOTICE.txt and APACHE-2.0.txt.\n\nArtworks are unmodified; filenames are normalized to Unicode code points.\nartwork-manifest.json records each source path, commit and checksum.\nNo emoji artwork is downloaded at runtime.\n`);
console.log(JSON.stringify({checked:false,catalogue:catalogue.version,fullyQualified:catalogue.rows.length,artworks:files.length,twemoji:files.filter(file=>file.source==='twemoji').length,noto:files.filter(file=>file.source==='noto').length,bytes:files.reduce((sum,file)=>sum+file.bytes,0),missing:0}));
