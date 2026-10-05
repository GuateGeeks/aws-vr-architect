// Run with the bundled Node runtime. Pass the path to its node_modules directory.
const fs = require('fs');
const path = require('path');
const sharp = require(require.resolve('sharp', { paths: [process.argv[2]] }));
const root = path.resolve(__dirname, '..');
const sources = {
  ApiGateway: ['Arch_Networking-Content-Delivery', 'Amazon-API-Gateway'],
  Lambda: ['Arch_Compute', 'AWS-Lambda'],
  DynamoDB: ['Arch_Databases', 'Amazon-DynamoDB'],
  S3: ['Arch_Storage', 'Amazon-Simple-Storage-Service'],
  SQS: ['Arch_Application-Integration', 'Amazon-Simple-Queue-Service'],
  EventBridge: ['Arch_Application-Integration', 'Amazon-EventBridge'],
  CloudWatch: ['Arch_Management-Tools', 'Amazon-CloudWatch']
};
const output = path.join(root, 'Assets/GuateGeeks/Resources/AwsIcons');
fs.mkdirSync(output, { recursive: true });
(async () => {
  for (const [kind, [category, service]] of Object.entries(sources)) {
    const source = path.join(root, 'aws-icons/Architecture-Service-Icons_07312026', category, '64', `Arch_${service}_64.svg`);
    // Preserve official paths and colors. Rasterize at 512px for Quest without an SVG runtime.
    await sharp(source, { density: 460.8 }).resize(512, 512).png().toFile(path.join(output, `${kind}.png`));
    console.log(`${kind}: ${path.relative(root, source)}`);
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
