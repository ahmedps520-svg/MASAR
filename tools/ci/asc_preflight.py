#!/usr/bin/env python3
"""
App Store Connect preflight, run by .github/workflows/asc-preflight.yml:
  * checks the API key works (and has enough access),
  * registers the bundle ID app.masar.game if it doesn't exist yet,
  * reports whether the App Store Connect app record exists (that record can only be
    created in the App Store Connect website; Apple offers no API for it).
Env: ASC_KEY_ID, ASC_ISSUER_ID, ASC_KEY_PATH, BUNDLE_ID
"""
import os, sys, time, json, urllib.request, urllib.error
import jwt

API = "https://api.appstoreconnect.apple.com/v1"


def token():
    key = open(os.environ["ASC_KEY_PATH"]).read()
    now = int(time.time())
    return jwt.encode({"iss": os.environ["ASC_ISSUER_ID"], "iat": now, "exp": now + 1100, "aud": "appstoreconnect-v1"},
                      key, algorithm="ES256", headers={"kid": os.environ["ASC_KEY_ID"], "typ": "JWT"})


def call(method, path, body=None):
    req = urllib.request.Request(API + path, method=method)
    req.add_header("Authorization", "Bearer " + token())
    req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, data=json.dumps(body).encode() if body else None, timeout=60) as r:
            raw = r.read()
            return r.status, (json.loads(raw) if raw else {})
    except urllib.error.HTTPError as e:
        raw = e.read().decode(errors="replace")
        try:
            return e.code, json.loads(raw)
        except Exception:
            return e.code, {"raw": raw}


def main():
    bundle = os.environ.get("BUNDLE_ID", "app.masar.game")
    st, apps = call("GET", "/apps?limit=1")
    if st == 401:
        print("::error::The API key was rejected (401). Check ASC_KEY_ID / ASC_ISSUER_ID / ASC_KEY_P8.")
        return 1
    print(f"API key OK ({st}).")

    st, ids = call("GET", f"/bundleIds?filter[identifier]={bundle}")
    exact = [d for d in ids.get("data", []) if d["attributes"].get("identifier") == bundle] if st == 200 else []
    if exact:
        print(f"Bundle ID {bundle} already registered (seedId/team prefix {exact[0]['attributes'].get('seedId')}).")
    else:
        st, r = call("POST", "/bundleIds", {"data": {"type": "bundleIds",
                     "attributes": {"identifier": bundle, "name": "MASAR", "platform": "IOS"}}})
        if st in (200, 201):
            print(f"Registered bundle ID {bundle}.")
        else:
            errs = "; ".join(f"{e.get('title')}: {e.get('detail')}" for e in r.get("errors", []))
            print(f"::error::Could not register {bundle} ({st}): {errs}. The key probably needs the Admin role.")
            return 1

    st, a = call("GET", f"/apps?filter[bundleId]={bundle}")
    if st == 200 and a.get("data"):
        print(f"App record exists: {a['data'][0]['attributes'].get('name')} ({a['data'][0]['id']}). Ready for TestFlight.")
        print("APP_RECORD=yes")
    else:
        print(f"::warning::No App Store Connect app record for {bundle} yet. Create it once: App Store Connect > Apps > + > New App.")
        print("APP_RECORD=no")
    return 0


if __name__ == "__main__":
    sys.exit(main())
