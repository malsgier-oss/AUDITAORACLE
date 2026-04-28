import fs from "fs";
const p = "g:/WorkAudit.CSharpOracle/Storage/Oracle/OracleSeedData.cs";
let s = fs.readFileSync(p, "utf8");
s = s.replace(/"([a-zA-Z_][a-zA-Z0-9_]*)"/g, "$1");
fs.writeFileSync(p, s);
console.log("ok");
