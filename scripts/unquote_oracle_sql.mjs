import fs from "fs";
const p2 = "g:/WorkAudit.CSharpOracle/Storage/Oracle/OracleBaselineInstaller.cs";
let s = fs.readFileSync(p2, "utf8");
s = s.replace(/"([a-zA-Z_][a-zA-Z0-9_]*)"/g, "$1");
fs.writeFileSync(p2, s);
console.log("ok");
