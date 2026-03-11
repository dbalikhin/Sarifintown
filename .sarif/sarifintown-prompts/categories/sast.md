# SAST Triage Directive

## Context & Reachability Check: SAST Vulnerability
The primary risk is execution. Context is everything. 

1.  **Understand the Reported Vulnerability:** Based on the provided rule name and message, identify the specific type of vulnerability being reported. All subsequent checks must focus on validating only this specific vulnerability type.

2.  **Evaluate Mitigating Controls and Execution Context:** Determine if the reported vulnerability is actually exploitable:
    * **Is the code reachable?** A finding in unreachable code (commented out, after a return) or in non-production files (tests, docs) is a False Positive.
    * **Is it Neutralized by Business Logic?** Analyze the if/else/switch statements that control access to the vulnerable code. The finding is often a False Positive if the vulnerable code is in a path that is:
        * **Guarded by Prior Validation:** Executed only after a reliable validation or sanitization check on the input has already passed.
        * **Debug/Test-Specific:** Confined to a non-production block, such as `if (DEBUG_MODE)` or a disabled feature flag.
    * **Is it properly sanitized or validated?** Look for strong, immediate controls that neutralize the risk, such as type casting & parsing (e.g., forcing input into a strict integer) or validating against existing data.
    * **Is it part of Exception Handling?** Examine if the finding is used within a `catch` or `except` block. Non-sensitive error codes or default values used only for logging on failure are common False Positives.

## Vulnerability-Specific Mitigations
Match the vulnerability type. Verify if the code uses these specific controls:

### SQL / NoSQL Injection
* **Type Casting:** Verify input is cast to a strict type (e.g., integer) before query execution.
* **ID Lookup:** Verify input acts only as a lookup key for existing records.
* **Parameterization:** Verify use of prepared statements or ORMs. Fail if input is concatenated.

### Cross-Site Scripting (XSS)
* **Auto-Escaping:** Check if the file is a UI template (`.jsx`, `.tsx`, `.vue`, `.svelte`).
* **Dangerous Overrides:** In UI templates, look for "raw html", "dangerously set", or "inner HTML". If absent, mark False Positive.
* **Non-HTML Response:** Verify the endpoint returns pure data (JSON/XML) without rendering HTML.
* **Explicit Encoding:** Look for HTML/URL encoding functions applied immediately before output.

### OS / Command Injection
* **Safe APIs:** Verify the execution API accepts arguments as a list/array, not a single concatenated string.
* **Allow-list:** Verify input must match a hardcoded list of allowed commands.

### Path Traversal / LFI / RFI
* **Normalization:** Verify the code resolves and removes relative path segments (e.g., `../`).
* **Base Directory:** Verify the resolved absolute path must start with a hardcoded, trusted directory.
* **Indirect Reference:** Verify input acts as a key to fetch a hardcoded path from a map/array.
* **Sanitization:** Look for explicit removal of `../` or `..\` strings.

### Server-Side Request Forgery (SSRF)
* **Host Validation:** Verify the URL protocol and host must match a hardcoded allow-list.
* **Internal Blocking:** Verify explicit blocks against loopback (`127.0.0.1`) and internal network IPs.

### XML External Entity (XXE)
* **Parser Hardening:** Verify the XML parser configuration explicitly disables DTDs and external entities.

### LDAP Injection
* **Encoding:** Verify input is wrapped in LDAP-specific encoding functions.
* **Alphanumeric Check:** Verify input is strictly validated (e.g., regex) to only allow alphanumeric characters.

### Insecure Deserialization
* **Data Format:** Verify the payload uses pure data formats (e.g., JSON) instead of native serialized objects.
* **Type Validation:** Verify the code uses allow-lists to strictly validate the class type before deserialization.