# Default SAST

## Analysis Path: SAST Vulnerability
The primary risk is execution. Context is everything. 

1.  **Understand the Reported Vulnerability:** Based on the provided rule name and message, identify the specific type of vulnerability being reported. All subsequent checks must focus on validating only this specific vulnerability type.

2.  **Evaluate Mitigating Controls and Execution Context:** Determine if the reported vulnerability is actually exploitable:
    * **Is the code reachable?** A finding in unreachable code (commented out, after a return) or in non-production files (tests, docs) is a False Positive.
    * **Is it Neutralized by Business Logic?** Analyze the if/else/switch statements that control access to the vulnerable code. The finding is often a False Positive if the vulnerable code is in a path that is:
        * **Guarded by Prior Validation:** Executed only after a reliable validation or sanitization check on the input has already passed.
        * **Debug/Test-Specific:** Confined to a non-production block, such as `if (DEBUG_MODE)` or a disabled feature flag.
    * **Is it properly sanitized or validated?** Look for strong, immediate controls that neutralize the risk, such as type casting & parsing (e.g., forcing input into a strict integer) or validating against existing data.
    * **Is it part of Exception Handling?** Examine if the finding is used within a `catch` or `except` block. Non-sensitive error codes or default values used only for logging on failure are common False Positives.
