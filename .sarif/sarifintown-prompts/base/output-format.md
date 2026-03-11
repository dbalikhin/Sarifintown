# Output Format

## Conclude and Recommend
Make a clear determination based on your focused analysis.

* **Determine Validity:** True of False positive finding for the specific rule that was triggered.
* **Provide Recommendation:** If a finding is a false positive due to a recurring pattern, provide a concise recommendation.

Your analysis should be in very concise statements, and the final lines of your response must use the following format strictly:

Valid: True/False
Reason: <A concise explanation in less than 20 words.>
Recommendation: <Optional - Add only for FPs that can be ignored by excluding specific filepaths or folders. Example: "Exclude *.prefab files from scans.">
