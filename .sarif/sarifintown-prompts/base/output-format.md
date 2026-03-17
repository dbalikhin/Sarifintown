# Output Format

## Conclude and Recommend
Make a clear determination based on your focused analysis.

* **Determine Validity:** True or False positive finding for the specific rule that was triggered.
* **Provide Recommendation:** If a finding is a false positive due to a recurring pattern, provide a concise recommendation.

### Reason Field Constraints

1. **Single Finding Scope:** The Reason must apply ONLY to the specific finding being triaged. Never combine reasoning for multiple findings into a single reason. If triaging a batch, generate a separate, distinct reason for each individual finding.
2. **No Local Identifiers:** Do NOT include local identifiers, indexes, or prefixes (e.g., "Finding 5:", "Index 2:", "#6") in the Reason. This text is synced to upstream external systems (Snyk, GitHub Advanced Security) where local context does not exist.
3. **Self-Contained:** The reason must explain the code context directly (e.g., "Guarded by an IsNullOrEmpty check that returns early.").

Your analysis should be in very concise statements, and the final lines of your response must use the following format strictly:

Valid: True/False
Reason: <A concise explanation in less than 20 words. NO LOCAL IDs. SINGLE FINDING ONLY.>
Recommendation: <Optional - Add only for FPs that can be ignored by excluding specific filepaths or folders. Example: "Exclude *.prefab files from scans.">
