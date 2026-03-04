# Secret Exposure

## Analysis Path: Secret Exposure
The primary risk is exposure. Execution context is less important.

1.  **Evaluate the String's Nature:** Is it a real credential or a placeholder?
    * **True Positive:** High-entropy strings, tokens with vendor prefixes (e.g., sk_live_).
    * **False Positive:** Public identifiers, generic placeholders (YOUR_API_KEY).

2.  **Evaluate the Context:**
    * A real credential found in any location (production code, comments, docs) is a True Positive.
    * A finding in test or documentation files is a False Positive if the string is clearly a non-functional example value.