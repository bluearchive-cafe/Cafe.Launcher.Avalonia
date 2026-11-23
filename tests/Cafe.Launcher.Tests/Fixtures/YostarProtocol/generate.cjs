// Offline reference vectors. No production .NET code or network is used.
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');

const hash = object => crypto.createHash('md5')
    .update(Object.values(object).join(';')).digest('base64');
const signed = (game, body) => {
    const head = { game_tag: game.tag, time: 1757600000, version: game.version };
    const sign = crypto.createHash('md5')
        .update(JSON.stringify(head) + body + game.salt).digest('hex');
    return JSON.stringify({ head, sign });
};
const ba = {
    tag: 'BlueArchive_JP', version: '1.7.2',
    api: 'https://api-launcher-jp.yo-star.com',
    salt: 'DE7108E9B2842FD460F4777702727869'
};
const stella = {
    tag: 'StellaSora_CN', version: '1.3.0',
    api: 'https://launcher-api.yostar.net',
    salt: '872550AD59A235662C5B7D5F88CEBE4B'
};
for (const [name, game, parameters] of [
    ['stella-legacy', stella, undefined],
    ['ba-empty-params', ba, []],
    ['ba-nonempty-params', ba, ['fixture-client.exe', '--fixture']]
]) {
    const config = { tag: game.tag, name: 'fixture-host' };
    if (parameters !== undefined) config.params = parameters;
    config.version = 'fixture-version';
    config.vc = hash(config);
    const reorderedConfig = { version: config.version, tag: config.tag, name: config.name };
    if (parameters !== undefined) reorderedConfig.params = parameters;
    reorderedConfig.vc = hash(reorderedConfig);
    const info = { name: game.tag, version: config.version, basis: 'fixture-basis' };
    const file = { path: '/data/fixture.bin', hash: '1234', size: '4' };
    file.vc = hash(file);
    const manifest = { ...info, vc: hash(info), files: [file] };
    const body = JSON.stringify({ game_tag: game.tag });
    const fixture = {
        synthetic: true,
        protocolVersion: game.version,
        apiBaseUrl: game.api,
        salt: game.salt,
        includesParameters: parameters !== undefined,
        unixTimeSeconds: 1757600000,
        authorization: [
            { body: '', expected: signed(game, '') },
            { body, expected: signed(game, body) }
        ],
        config,
        reorderedConfig,
        manifest
    };
    if (name === 'ba-empty-params') {
        fixture.invalidManifestFiles = {};
        for (const [vectorName, property, value] of [
            ['size-negative', 'size', '-1'],
            ['size-signed', 'size', '+4'],
            ['size-fraction', 'size', '4.0'],
            ['size-whitespace', 'size', ' 4'],
            ['size-overflow', 'size', '9223372036854775808'],
            ['hash-negative', 'hash', '-1'],
            ['hash-signed', 'hash', '+1234'],
            ['hash-whitespace', 'hash', '1234 '],
            ['hash-overflow', 'hash', '18446744073709551616']
        ]) {
            const invalidFile = { path: file.path, hash: file.hash, size: file.size };
            invalidFile[property] = value;
            invalidFile.vc = hash(invalidFile);
            fixture.invalidManifestFiles[vectorName] = invalidFile;
        }
    }
    fs.writeFileSync(path.join(__dirname, name + '.json'), JSON.stringify(fixture, null, 2) + '\n');
}
