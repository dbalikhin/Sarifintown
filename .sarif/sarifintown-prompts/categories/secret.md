# Secret Exposure Triage Directive

## Analysis Path: Secret Exposure
You are analyzing a Hardcoded Secret finding. The primary risk is exposure. Execution context is less important.

## 1. Evaluate String Entropy and Nature
Determine if the string is functional.
* **True Positive:** Verify if the string has high entropy, structured vendor formats, or prefixes (e.g., `sk_live_`, `xoxb-`, `ghp_`).
* **False Positive:** Verify if the string is a public identifier, generic placeholder (e.g., `YOUR_API_KEY`, `password123`), or standard cryptographic algorithm name.

## 2. Evaluate Environment Context
Determine where the string is located.
* **True Positive:** A real, high-entropy credential is a True Positive regardless of location (production code, commented-out code, test files, or documentation).
* **False Positive:** A finding in a test or documentation file is only a False Positive if the string is clearly a mock value or non-functional example.
