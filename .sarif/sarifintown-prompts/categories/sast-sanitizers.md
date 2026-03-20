### Custom SAST Sanitizers
The following functions and methods are approved custom sanitizers for our application. If tainted data passes through any of these functions before reaching a dangerous sink, the data flow is considered safe.

**Assessment Instruction:** If you observe the tainted input being passed into a documented sanitizer within the data flow evidence, you MUST mark the finding as a **False Positive**, as the risk has been mitigated.

*(Note: Add custom organization-specific sanitizer functions below)*
