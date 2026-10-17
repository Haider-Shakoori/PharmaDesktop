"""Guard packaged Windows defaults against legacy cloud hosts."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
canonical = "https://darmaltoon.com"
for path in (
    "src/BusinessOS.Pharmacy.Desktop/appsettings.json",
    "src/BusinessOS.Pharmacy.LocalServer/appsettings.json",
):
    config = json.loads((root / path).read_text(encoding="utf-8"))
    license_settings = config["BusinessOS"]["Licensing"]
    assert license_settings["BaseUrl"] == canonical, path
    assert license_settings["ActivationPath"] == "/api/v1/license/activate"
    assert license_settings["RefreshPath"] == "/api/v1/desktop/license/refresh"

desktop = json.loads(
    (root / "src/BusinessOS.Pharmacy.Desktop/appsettings.json").read_text(encoding="utf-8")
)["BusinessOS"]
assert desktop["Sync"]["BaseUrl"] == canonical
assert desktop["Updater"]["ManifestUrl"] == canonical + "/api/v1/desktop/update/manifest"
assert desktop["Licensing"]["SigningPublicKey"]
assert desktop["Updater"]["SigningPublicKeyPem"].startswith("-----BEGIN PUBLIC KEY-----")
print("Darmaltoon Windows canonical API configuration and signing key presence verified.")
