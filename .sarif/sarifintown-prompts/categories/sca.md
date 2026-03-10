# SCA Triage Directive

You are analyzing a Software Composition Analysis (SCA) finding for a third-party open-source dependency. Evaluate the following to determine validity.

## 1. Scope and Deployment Check
Determine if the vulnerable package is actually shipped to production.
* **Production Context:** Verify if the dependency is compiled and shipped in the final product or executed on the server.
* **Non-Production Scope:** Verify if the package is strictly used for test code, mocks, documentation, or local build processes (e.g., `devDependencies`, test scopes). If YES: Mark False Positive.

## 2. Exploitability and Reachability
Determine if the specific vulnerability within the package is exploitable in the current application context.
* **Function Reachability:** Verify if the application explicitly imports and invokes the specific vulnerable function, class, or module from the library. If the package is installed but the vulnerable component is never called: Mark False Positive.
* **Environmental Prerequisites:** Verify if the CVE requires a specific operating system, architecture, or runtime version (e.g., Windows-only flaws) that does not match the target deployment environment.
* **Configuration Prerequisites:** Verify if the vulnerability requires a non-default configuration, specific module, or feature flag of the package to be enabled. 
* **Data Flow Exposure:** For Denial of Service (DoS) or parsing vulnerabilities, verify if the application actually passes untrusted, external user-supplied data into the vulnerable library function. If the library only processes trusted/hardcoded internal data: Mark False Positive.