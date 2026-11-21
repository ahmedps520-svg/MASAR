#!/usr/bin/env python3
"""
After an upload, make the build installable on the owner's iPad with no manual steps:
  1. wait until App Store Connect finishes processing the build,
  2. mark export compliance (the app uses no non-exempt encryption),
  3. ensure an internal TestFlight group with access to all builds,
  4. add the tester email (from the TESTER_EMAIL secret) to that group.

Internal TestFlight testers must be users of the App Store Connect team. The account holder
always is, so this works when TESTER_EMAIL is the account holder's Apple ID. Otherwise the
script says so clearly instead of pretending.

Env: ASC_KEY_ID, ASC_ISSUER_ID, ASC_KEY_PATH, TESTER_EMAIL, BUNDLE_ID, BUILD_NUMBER
Uses only PyJWT + cryptography + the standard library.
"""
import json, os, sys, time, urllib.request, urllib.error
import jwt  # PyJWT

API = "https://api.appstoreconnect.apple.com/v1"


def token():
    with open(os.environ["ASC_KEY_PATH"], "r") as f:
        key = f.read()
    now = int(time.time())
    return jwt.encode({"iss": os.environ["ASC_ISSUER_ID"], "iat": now, "exp": now + 1100, "aud": "appstoreconnect-v1"},
                      key, algorithm="ES256", headers={"kid": os.environ["ASC_KEY_ID"], "typ": "JWT"})


def call(method, path, body=None):
    req = urllib.request.Request(API + path if path.startswith("/") else path, method=method)
    req.add_header("Authorization", "Bearer " + token())
    req.add_header("Content-Type", "application/json")
    data = json.dumps(body).encode() if body is not None else None
    try:
        with urllib.request.urlopen(req, data=data, timeout=60) as r:
            raw = r.read()
            return r.status, (json.loads(raw) if raw else {})
    except urllib.error.HTTPError as e:
        raw = e.read().decode(errors="replace")
        try:
            return e.code, json.loads(raw)
        except Exception:
            return e.code, {"raw": raw}


def errors_text(resp):
    errs = resp.get("errors") or []
    return "; ".join(f"{e.get('title')}: {e.get('detail')}" for e in errs) or json.dumps(resp)[:400]


def main():
    bundle = os.environ.get("BUNDLE_ID", "app.masar.game")
    build_number = os.environ.get("BUILD_NUMBER", "")
    email = os.environ.get("TESTER_EMAIL", "").strip()

    st, apps = call("GET", f"/apps?filter[bundleId]={bundle}")
    if st != 200 or not apps.get("data"):
        print(f"::error::App record for {bundle} not found in App Store Connect ({st}). Create it once in App Store Connect.")
        return 1
    app_id = apps["data"][0]["id"]
    print(f"App {bundle} -> {app_id}")

    # 1) wait for processing
    build_id, deadline = None, time.time() + 45 * 60
    while time.time() < deadline:
        q = f"/builds?filter[app]={app_id}&sort=-uploadedDate&limit=5"
        if build_number:
            q += f"&filter[version]={build_number}"
        st, builds = call("GET", q)
        if st == 200 and builds.get("data"):
            b = builds["data"][0]
            state = b["attributes"].get("processingState")
            build_id = b["id"]
            print(f"Build {b['attributes'].get('version')} processingState={state}")
            if state == "VALID":
                break
            if state in ("FAILED", "INVALID"):
                print(f"::error::Build processing {state}")
                return 1
        else:
            print(f"Waiting for the build to appear ({st})…")
        time.sleep(60)
    else:
        print("::error::Timed out waiting for build processing")
        return 1

    # 2) export compliance (Info.plist already declares ITSAppUsesNonExemptEncryption=false)
    st, b = call("GET", f"/builds/{build_id}")
    if st == 200 and b["data"]["attributes"].get("usesNonExemptEncryption") is None:
        st2, r = call("PATCH", f"/builds/{build_id}", {"data": {"type": "builds", "id": build_id,
                                                                "attributes": {"usesNonExemptEncryption": False}}})
        print(f"Export compliance set: {st2}")

    # 3) internal group with access to all builds
    group_name = "MASAR Internal"
    st, groups = call("GET", f"/betaGroups?filter[app]={app_id}&filter[name]={urllib.request.quote(group_name)}")
    if st == 200 and groups.get("data"):
        group_id = groups["data"][0]["id"]
    else:
        st, g = call("POST", "/betaGroups", {"data": {"type": "betaGroups",
                     "attributes": {"name": group_name, "isInternalGroup": True, "hasAccessToAllBuilds": True},
                     "relationships": {"app": {"data": {"type": "apps", "id": app_id}}}}})
        if st not in (200, 201):
            print(f"::error::Could not create internal group: {errors_text(g)}")
            return 1
        group_id = g["data"]["id"]
    print(f"Internal group -> {group_id}")

    # 4) tester
    if not email:
        print("::warning::TESTER_EMAIL secret not set; skipping the invite")
        return 0
    st, existing = call("GET", f"/betaTesters?filter[email]={urllib.request.quote(email)}&filter[betaGroups]={group_id}")
    if st == 200 and existing.get("data"):
        print("Tester already in the internal group. The new build is available in TestFlight.")
        return 0
    st, t = call("POST", "/betaTesters", {"data": {"type": "betaTesters",
                 "attributes": {"email": email, "firstName": "MASAR", "lastName": "Tester"},
                 "relationships": {"betaGroups": {"data": [{"type": "betaGroups", "id": group_id}]}}}})
    if st in (200, 201):
        print("Tester invited to the internal group. Apple emails a TestFlight invite.")
        return 0
    print(f"::warning::TESTER_EMAIL could not be added ({errors_text(t)}). Internal testers must be "
          "App Store Connect users of this team; falling back to the team's account holder.")
    # Fallback: the account holder (the developer account's own Apple ID). Never printed: CI logs are public.
    st, users = call("GET", "/users?limit=200")
    holder = None
    if st == 200:
        for u in users.get("data", []):
            roles = u.get("attributes", {}).get("roles", []) or []
            if "ACCOUNT_HOLDER" in roles:
                holder = u
                break
    if holder is None:
        print(f"::warning::Could not read the team's users ({st}). The build is in TestFlight: add yourself to the "
              f"'{group_name}' group in App Store Connect > TestFlight.")
        return 0
    a = holder["attributes"]
    st, existing = call("GET", f"/betaTesters?filter[email]={urllib.request.quote(a.get('username', ''))}&filter[betaGroups]={group_id}")
    if st == 200 and existing.get("data"):
        print("Account holder already in the internal group. The new build is available in TestFlight.")
        return 0
    st, t = call("POST", "/betaTesters", {"data": {"type": "betaTesters",
                 "attributes": {"email": a.get("username"), "firstName": a.get("firstName") or "MASAR",
                                "lastName": a.get("lastName") or "Tester"},
                 "relationships": {"betaGroups": {"data": [{"type": "betaGroups", "id": group_id}]}}}})
    if st in (200, 201):
        print("Account holder added to the internal group. The build appears in the TestFlight app on devices "
              "signed in with that Apple ID (Apple also emails an invite).")
        return 0
    print(f"::warning::Could not add the account holder ({errors_text(t)}). The build is in TestFlight: add yourself "
          f"to the '{group_name}' group in App Store Connect > TestFlight.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
