#!/usr/bin/env python3
"""
Keep the Apple signing certificates that MASAR's CI creates from piling up.

Every macOS runner starts with an empty keychain, so Xcode's automatic signing (API key +
-allowProvisioningUpdates) creates a fresh development and distribution certificate on each
run. They are useless once the runner is gone, and Apple caps how many a team may have.

This script only ever touches certificates MASAR's own runs created. Certificates made by
other apps, other CI systems or by hand on a Mac are never revoked.

  snapshot FILE        save the IDs of the certificates that exist now (before signing)
  own FILE [--revoke]  certificates created during this run: not in FILE AND their key pair
                       is in this runner's keychain (Xcode generated it here)
  old WINDOWS [--revoke]
                       certificates issued while one of this repository's past signing runs
                       was running (WINDOWS: JSON list of {id, start, end} in UTC ISO 8601)
                       and named "Created via API" (made with an API key, not on a Mac)

Without --revoke nothing is changed. The log shows type, ID and issue time only, never the
certificate's name, which can contain the account holder's name.

Env: ASC_KEY_ID, ASC_ISSUER_ID, ASC_KEY_PATH. Needs PyJWT + cryptography.
"""
import base64, datetime as dt, json, subprocess, sys
from cryptography import x509
from cryptography.x509.oid import NameOID

sys.path.insert(0, __file__.rsplit("/", 1)[0])
from testflight_invite import call, errors_text  # same API key handling

SLACK = dt.timedelta(minutes=2)


def certificates():
    out, url = [], "/certificates?limit=200"
    while url:
        st, r = call("GET", url)
        if st != 200:
            raise SystemExit(f"::error::Listing certificates failed ({st}): {errors_text(r)}")
        for c in r.get("data", []):
            a = c["attributes"]
            cert = x509.load_der_x509_certificate(base64.b64decode(a["certificateContent"]))
            cn = cert.subject.get_attributes_for_oid(NameOID.COMMON_NAME)
            out.append({"id": c["id"], "type": a.get("certificateType"),
                        "issued": cert.not_valid_before_utc, "serial": cert.serial_number,
                        "api": bool(cn) and "Created via API" in cn[0].value})
        url = r.get("links", {}).get("next")
    return out


def keychain_serials():
    pem = subprocess.run(["security", "find-certificate", "-a", "-p"],
                         capture_output=True, text=True).stdout
    serials, block = set(), []
    for line in pem.splitlines():
        block.append(line)
        if line.startswith("-----END CERTIFICATE"):
            try:
                serials.add(x509.load_pem_x509_certificate("\n".join(block).encode()).serial_number)
            except ValueError:
                pass
            block = []
    return serials


def show(c, note=""):
    print(f"  {c['type']:<24} {c['id']}  issued {c['issued']:%Y-%m-%d %H:%M:%S}Z  api={c['api']} {note}")


def revoke(chosen, really):
    if not chosen:
        print("Nothing to revoke.")
        return 0
    failed = 0
    for c in chosen:
        if not really:
            show(c, "(would revoke)")
            continue
        st, r = call("DELETE", f"/certificates/{c['id']}")
        if st in (200, 204):
            show(c, "revoked")
        else:
            failed += 1
            show(c, f"NOT revoked ({st}: {errors_text(r)})")
    print(f"{'Revoked' if really else 'Would revoke'} {len(chosen) - failed} of {len(chosen)}.")
    return 1 if failed else 0


def main(argv):
    mode, arg, really = argv[1], argv[2], "--revoke" in argv
    certs = certificates()
    print(f"{len(certs)} certificates in the team.")
    if mode == "snapshot":
        with open(arg, "w") as f:
            json.dump([c["id"] for c in certs], f)
        return 0
    if mode == "own":
        with open(arg) as f:
            before = set(json.load(f))
        here = keychain_serials()
        return revoke([c for c in certs if c["id"] not in before and c["serial"] in here], really)
    if mode == "old":
        with open(arg) as f:
            windows = [(w["id"], dt.datetime.fromisoformat(w["start"].replace("Z", "+00:00")),
                        dt.datetime.fromisoformat(w["end"].replace("Z", "+00:00"))) for w in json.load(f)]
        chosen, others = [], 0
        for c in sorted(certs, key=lambda c: c["issued"]):
            run = next((i for i, s, e in windows if s - SLACK <= c["issued"] <= e + SLACK), None)
            if run and c["api"]:
                chosen.append(c)
            elif run:
                show(c, f"issued during run {run} but not made with an API key: kept")
            else:
                others += 1
                show(c, "not issued during a MASAR run: kept")
        print(f"{others} certificates were not issued during a MASAR run: kept.")
        return revoke(chosen, really)
    raise SystemExit(f"unknown mode {mode}")


if __name__ == "__main__":
    sys.exit(main(sys.argv))
