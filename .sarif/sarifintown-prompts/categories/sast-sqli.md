# SAST SQL Injection

## Analysis Path: SQL Injection (SQLi)
All subsequent checks must focus on validating specifically for SQL Injection.

**Evaluate Mitigating Controls and Execution Context:** Determine if the reported vulnerability is actually exploitable.
* **Is the code reachable?** A finding in unreachable code (commented out, after a return) or in non-production files (tests, docs) is a False Positive.
* **Is it properly sanitized or validated?** Look for strong, immediate controls that neutralize the SQL injection risk:
    * **Type Casting & Parsing:** Is the input forced into a strict data type, like an integer (e.g., `Integer.parseInt(userInput)`)? This is a strong form of validation against SQLi.
    * **Database ID Lookup:** Is the input used as an ID to fetch a record from a database (e.g., `product = db.getProductById(userInput)`)? This validates the input against existing data and often neutralizes the threat.
* **Is it Neutralized by Business Logic?** The finding is often a False Positive if the vulnerable code is in a path that is Guarded by Prior Validation or confined to a Debug/Test-Specific block.
