import re
path = r"g:/WorkAudit.CSharpOracle/Storage/Oracle/OracleBaselineInstaller.cs"
s = open(path, encoding="utf-8").read()
s = re.sub(r'"([a-zA-Z_][a-zA-Z0-9_]*)"', r"\1", s)
open(path, "w", encoding="utf-8").write(s)
print("done")
