import subprocess
import json
import urllib.request
import re

MODEL = "gemma:2b"

def run(cmd):
    return subprocess.getoutput(cmd)

def ask_gemma(prompt):
    try:
        data = json.dumps({
            "model": MODEL,
            "prompt": prompt[:1000],
            "stream": False
        }).encode("utf-8")

        req = urllib.request.Request(
            "http://localhost:11434/api/generate",
            data=data,
            headers={"Content-Type": "application/json"}
        )

        response = urllib.request.urlopen(req)
        result = json.loads(response.read().decode())

        return result.get("response", "")

    except Exception as e:
        return f"ERROR: {str(e)}"


def apply_fix(response):
    """
    Looks for:
    FILE: path
    CODE:
    <code>
    """

    pattern = r"FILE:\s*(.*?)\nCODE:\n([\s\S]*?)(?=FILE:|$)"
    matches = re.findall(pattern, response)

    for file_path, code in matches:
        file_path = file_path.strip()

        try:
            with open(file_path, "w", encoding="utf-8") as f:
                f.write(code.strip())

            print(f"✅ Updated: {file_path}")

        except Exception as e:
            print(f"❌ Failed writing {file_path}: {e}")


while True:
    print("\n🚀 AI Agent Running...\n")

    # Run build
    build_output = run("dotnet build --no-restore")

    if "error" in build_output.lower():
        print("❌ Errors found. Asking AI...\n")

        prompt = f"""
You are a senior .NET developer.

Fix the error below.

IMPORTANT:
Return in this format ONLY:

FILE: path/to/file.cs
CODE:
<full updated code>

Error:
{build_output[-800:]}
"""

        response = ask_gemma(prompt)

        print("💡 AI Response:\n")
        print(response)

        # Apply fix
        apply_fix(response)

    else:
        print("✅ Build successful!")

        run("git add .")
        run('git commit -m "AI auto fix successful"')

        print("📦 Code committed!")

        break