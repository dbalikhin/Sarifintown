# .NET 10.0 Upgrade Plan

## Table of Contents

- [Executive Summary](#executive-summary)
- [Migration Strategy](#migration-strategy)
- [Detailed Dependency Analysis](#detailed-dependency-analysis)
- [Implementation Timeline](#implementation-timeline)
- [Detailed Execution Steps](#detailed-execution-steps)
- [Project-by-Project Migration Plans](#project-by-project-migration-plans)
- [Package Update Reference](#package-update-reference)
- [Breaking Changes Catalog](#breaking-changes-catalog)
- [Testing & Validation Strategy](#testing--validation-strategy)
- [Risk Management](#risk-management)
- [Complexity & Effort Assessment](#complexity--effort-assessment)
- [Source Control Strategy](#source-control-strategy)
- [Success Criteria](#success-criteria)

---

## Executive Summary

### Scenario Overview

Upgrade Sarifintown Blazor WebAssembly solution from .NET 9.0 to .NET 10.0 (Long Term Support).

### Scope

- **Total Projects:** 2
  - Sarifintown\Sarifintown.csproj (Blazor WebAssembly application)
  - Sarifintown.Tests\Sarifintown.Tests.csproj (Test project)
- **Current State:** Both projects targeting net9.0
- **Target State:** Both projects targeting net10.0
- **Total Codebase:** 1,641 lines of code across 22 files
- **Estimated Impact:** 11+ lines of code modification (0.7% of codebase)

### Complexity Classification

**Classification: Simple Solution**

**Metrics:**
- 2 projects (well below 5-project threshold)
- Depth: 1 level (Tests → Main)
- Both projects: Low difficulty rating
- No high-risk projects
- No security vulnerabilities identified
- Clean dependency structure
- All packages have compatible versions available

### Selected Strategy

**All-At-Once Strategy** - All projects upgraded simultaneously in a single coordinated operation.

### Rationale

1. **Small Solution Size:** Only 2 projects - well within the ideal range (<5 projects) for atomic upgrades
2. **Homogeneous Framework Base:** Both projects currently on net9.0 (no mixed .NET Framework/Core scenarios)
3. **Clean Dependency Structure:** Simple linear dependency (Tests → Main application) with no circular dependencies
4. **Low Complexity:** Both projects rated as "Low" difficulty with minimal code impact (11 LOC across entire solution)
5. **Package Compatibility:** All 4 required package updates have clear target versions available (9.0.8 → 10.0.3)
6. **No Security Blockers:** Zero security vulnerabilities requiring separate remediation
7. **Minimal Breaking Changes:** Only 2 critical API issues (1 binary incompatible, 1 source incompatible) affecting limited code surface

### Critical Considerations

- **No Intermediate States:** Both projects and all packages updated in one atomic operation
- **Unified Testing:** All changes validated together after complete upgrade
- **Fast Execution:** Shorter total timeline compared to incremental approach
- **Single Coordination Point:** One comprehensive commit with all framework/package changes

### Expected Iteration Strategy

**Fast Batch Approach (2-3 iterations):**
- Foundation iterations establish structure and strategy
- Single detail iteration covers both projects (simple complexity enables batching)
- Final iteration completes validation criteria and source control strategy

---

## Migration Strategy

### Approach Selection

**All-At-Once Migration**

All projects in the solution will be upgraded simultaneously in a single atomic operation.

### Rationale

**Why All-At-Once:**
1. **Solution Size:** 2 projects - significantly below the 5-project threshold for atomic upgrades
2. **Framework Homogeneity:** Both projects currently on net9.0, avoiding .NET Framework → Core migration complexity
3. **Minimal Code Impact:** Only 11 lines estimated for modification (0.7% of codebase)
4. **Low Risk Profile:** Both projects rated "Low" difficulty, no high-risk components identified
5. **Clear Package Upgrade Path:** All 4 package updates have well-defined target versions
6. **Strong Test Coverage:** Dedicated test project (Sarifintown.Tests) enables comprehensive validation

**Why Not Incremental:**
- Unnecessary complexity for such a small solution
- Would create temporary multi-targeting overhead without benefit
- Longer total timeline for minimal risk reduction
- Additional coordination burden for 2-project solution

### Execution Approach

**Atomic Operation Sequence:**

1. **Simultaneous Framework Update**
   - Update TargetFramework to net10.0 in both project files

2. **Unified Package Update**
   - Update all 4 Microsoft packages to version 10.0.3 across affected projects

3. **Comprehensive Build**
   - Restore dependencies for entire solution
   - Build complete solution to surface all compilation errors

4. **Atomic Fix Phase**
   - Address all compilation errors from framework/package changes
   - Fix binary incompatible API (ConfigurationBinder.GetValue)
   - Fix source incompatible API (Path.Combine)
   - Rebuild solution to verify fixes

5. **Validation**
   - Execute all tests in Sarifintown.Tests.csproj
   - Verify behavioral changes in System.Uri usage
   - Confirm no runtime regressions

### Dependency-Based Ordering

While all updates occur simultaneously, the validation order respects dependencies:

- **Update:** Both projects' TargetFramework and packages modified together
- **Build:** Entire solution built as unit (implicit dependency ordering handled by MSBuild)
- **Validation:** Main application must build successfully before test project can execute

### Risk Mitigation for Atomic Approach

1. **Single Commit Strategy:** All changes in one commit enables easy rollback if needed
2. **Pre-Build Validation:** Ensure .NET 10 SDK installed before starting
3. **Breaking Changes Review:** Catalog all known breaking changes before modification
4. **Test-First Verification:** Run existing tests after build to catch regressions early
5. **Behavioral Change Testing:** Specific attention to System.Uri and HttpClient changes identified in assessment

### Parallel vs Sequential

**Not Applicable:** With only 2 projects in atomic upgrade, parallelization is not a consideration. Both projects are updated in the same operation, and MSBuild handles build ordering automatically based on project dependencies.

---

## Detailed Dependency Analysis

### Dependency Graph Summary

```
Sarifintown.csproj (net9.0)
    ↑
    └── Sarifintown.Tests.csproj (net9.0)
```

**Characteristics:**
- **Leaf Node:** Sarifintown.csproj (no dependencies, 1 dependant)
- **Root Node:** Sarifintown.Tests.csproj (1 dependency, no dependants)
- **Depth:** 1 level
- **Circular Dependencies:** None

### Migration Phase Grouping

**All-At-Once: Single Atomic Phase**

All projects are upgraded simultaneously as part of one coordinated operation. No phased approach needed due to simple structure and low complexity.

**Projects Included in Atomic Upgrade:**
1. Sarifintown\Sarifintown.csproj (Blazor WebAssembly application)
2. Sarifintown.Tests\Sarifintown.Tests.csproj (Test project)

### Critical Path

Since all projects upgrade simultaneously, there is no critical path in the traditional sense. The dependency relationship (Tests → Main) is respected during validation:

1. **Update Phase:** All project files and packages updated together
2. **Build Phase:** Entire solution built as a unit
3. **Fix Phase:** Compilation errors addressed across all projects
4. **Test Phase:** Tests run after main application successfully builds

### Dependency Considerations

- **Test Project Dependency:** Sarifintown.Tests depends on Sarifintown, so the main application must build successfully before tests can execute
- **No Blocking Dependencies:** No external project dependencies that could block upgrade
- **Package Alignment:** All Microsoft framework packages will move to 10.0.3 together, ensuring version consistency

---

## Implementation Timeline

### Phase 0: Preparation

**Operations:**
- Verify .NET 10 SDK installed on development machine
- Ensure solution builds successfully on current net9.0 configuration
- Review .NET 10 breaking changes documentation
- Create backup or ensure version control is clean

**Deliverables:**
- .NET 10 SDK available (`dotnet --list-sdks` shows 10.x)
- Current solution builds with 0 errors
- Breaking changes documentation reviewed
- Ready to begin upgrade

**Estimated Complexity:** Low

---

### Phase 1: Atomic Upgrade

**Operations** (performed as single coordinated batch):

1. **Update Project Files**
   - Modify Sarifintown\Sarifintown.csproj TargetFramework to net10.0
   - Modify Sarifintown.Tests\Sarifintown.Tests.csproj TargetFramework to net10.0

2. **Update Package References**
   - Update 4 packages in Sarifintown.csproj to version 10.0.3:
     - Microsoft.AspNetCore.Components.WebAssembly
     - Microsoft.AspNetCore.Components.WebAssembly.DevServer
     - Microsoft.Extensions.Http
     - System.Text.Json

3. **Restore Dependencies**
   - Execute `dotnet restore` for entire solution

4. **Build Solution**
   - Execute `dotnet build` to identify compilation errors
   - Expected errors:
     - Path.Combine(ReadOnlySpan<string>) source incompatibility
     - ConfigurationBinder.GetValue binary incompatibility (may appear as runtime issue)

5. **Fix Compilation Errors**
   - Fix Path.Combine usage in Sarifintown.Tests
   - Fix ConfigurationBinder.GetValue usage in Sarifintown.Tests
   - Address any other compilation errors surfaced by build

6. **Rebuild and Verify**
   - Execute `dotnet build` to confirm all fixes applied
   - Verify solution builds with 0 errors
   - Address any warnings if critical

**Deliverables:**
- Both projects targeting net10.0
- All packages updated to target versions
- Solution builds successfully with 0 errors

**Estimated Complexity:** Low

---

### Phase 2: Testing & Validation

**Operations:**

1. **Execute Test Suite**
   - Run all tests in Sarifintown.Tests.csproj
   - Identify any test failures caused by framework changes

2. **Address Test Failures**
   - Fix tests broken by behavioral changes
   - Update test assertions if framework behavior legitimately changed
   - Verify no regressions in application logic

3. **Manual Application Validation**
   - Run Blazor WebAssembly application
   - Test primary user workflows:
     - Load SARIF file
     - View analysis results
     - Extract code snippets
     - Navigate between issues
   - Verify JS interop functionality
   - Check browser console for errors

4. **Behavioral Change Verification**
   - Specifically test System.Uri usage scenarios
   - Verify HttpClient functionality
   - Check configuration loading (if applicable in main app)
   - Validate file path handling

**Deliverables:**
- All tests pass
- Application runs without errors
- Primary features validated
- No behavioral regressions detected

**Estimated Complexity:** Low

---

### Timeline Summary

| Phase | Focus | Complexity | Dependencies |
|-------|-------|-----------|--------------|
| **Phase 0: Preparation** | SDK verification, documentation review | Low | None |
| **Phase 1: Atomic Upgrade** | Framework + packages + code fixes | Low | Phase 0 complete |
| **Phase 2: Testing & Validation** | Automated tests + manual verification | Low | Phase 1 complete |

**All-At-Once Characteristics:**
- No intermediate states - all changes applied together
- Single build/fix cycle for all projects
- Unified validation after complete upgrade
- Single commit containing all changes

---

## Detailed Execution Steps

### Step 1: Verify Prerequisites

**Before starting upgrade:**

1. **Check .NET 10 SDK Installation:**
   ```bash
   dotnet --list-sdks
   ```
   Expected: Output includes `10.0.xxx` SDK

2. **Verify Current Build:**
   ```bash
   dotnet build Sarifintown.sln
   ```
   Expected: Build succeeds with 0 errors

3. **Review Documentation:**
   - [.NET 10 Breaking Changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0)
   - Focus on: System.Uri, ConfigurationBinder, Path APIs
   - Review Blazor WebAssembly changes

4. **Version Control Status:**
   - Ensure working directory is clean or changes are committed
   - Ready to create single atomic commit for upgrade

---

### Step 2: Update All Project Files

**Update both project files simultaneously:**

#### File: `Sarifintown\Sarifintown.csproj`

Locate:
```xml
<TargetFramework>net9.0</TargetFramework>
```

Change to:
```xml
<TargetFramework>net10.0</TargetFramework>
```

#### File: `Sarifintown.Tests\Sarifintown.Tests.csproj`

Locate:
```xml
<TargetFramework>net9.0</TargetFramework>
```

Change to:
```xml
<TargetFramework>net10.0</TargetFramework>
```

---

### Step 3: Update All Package References

**Update 4 packages in `Sarifintown\Sarifintown.csproj`:**

Locate each PackageReference and update version to `10.0.3`:

```xml
<PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.3" />
<PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="10.0.3" />
<PackageReference Include="Microsoft.Extensions.Http" Version="10.0.3" />
<PackageReference Include="System.Text.Json" Version="10.0.3" />
```

**Note:** No package updates in Sarifintown.Tests.csproj - all test packages remain on current versions.

---

### Step 4: Restore Dependencies

Execute dependency restoration for entire solution:

```bash
dotnet restore Sarifintown.sln
```

**Expected Output:**
- All packages download successfully
- No restore errors or warnings

**If Errors Occur:**
- Check package version availability (10.0.3 should be latest stable)
- Verify NuGet sources configured correctly
- Clear NuGet cache if needed: `dotnet nuget locals all --clear`

---

### Step 5: Build Solution and Identify Errors

Execute full solution build:

```bash
dotnet build Sarifintown.sln
```

**Expected Errors:**

1. **Path.Combine Source Incompatibility** (Sarifintown.Tests):
   - Error code: CS1503 or similar
   - Message: Cannot convert from 'ReadOnlySpan<string>' to 'string[]'
   - Location: 1 file in test project

2. **ConfigurationBinder.GetValue** (Sarifintown.Tests):
   - May appear as compilation error or warning
   - Depends on specific .NET 10 change
   - Location: 1 file in test project

**Note Breaking Changes Guidance:**
- Review §Breaking Changes Catalog for fix approaches
- Document exact error messages for reference

---

### Step 6: Fix Compilation Errors

#### Fix 1: Path.Combine(ReadOnlySpan<string>)

**Locate usage in Sarifintown.Tests:**
```bash
grep -r "Path.Combine" Sarifintown.Tests/
```

**Apply fix** (see §Breaking Changes Catalog for options):
```csharp
// Example fix - convert span to array:
var combined = Path.Combine(pathSegments.ToArray());
```

#### Fix 2: ConfigurationBinder.GetValue<T>

**Locate usage in Sarifintown.Tests:**
```bash
grep -r "GetValue" Sarifintown.Tests/
```

**Apply fix** (consult .NET 10 docs for specific change):
```csharp
// Example - may need to add default parameter or use alternative API
var value = config.GetValue<string>("key", defaultValue: null);
```

---

### Step 7: Rebuild and Verify

Execute build again to confirm fixes:

```bash
dotnet build Sarifintown.sln
```

**Expected Outcome:**
- Build succeeds with 0 errors
- Warnings acceptable if not critical (review individually)

**If Build Still Fails:**
- Review error messages against .NET 10 breaking changes
- Consult §Breaking Changes Catalog
- Search for specific error codes in .NET 10 migration documentation

---

### Step 8: Execute All Tests

Run full test suite:

```bash
dotnet test Sarifintown.sln
```

**Expected Outcome:**
- All tests pass
- 0 failed, 0 skipped

**If Tests Fail:**

1. **Review Failure Details:**
   - Identify which tests failed
   - Determine if failure related to code changes or behavioral changes

2. **Categorize Failures:**
   - **Configuration-related:** May be ConfigurationBinder fix issue
   - **Path-related:** May be Path.Combine fix issue
   - **URI-related:** Likely behavioral change, verify expected behavior
   - **HTTP-related:** Likely HttpClient behavioral change

3. **Fix Test Failures:**
   - Update test code if application logic correct
   - Fix application code if regression detected
   - Update test assertions if .NET 10 behavior legitimately changed

4. **Rerun Tests:**
   ```bash
   dotnet test Sarifintown.sln
   ```

---

### Step 9: Manual Application Validation

Run Blazor WebAssembly application:

```bash
dotnet run --project Sarifintown\Sarifintown.csproj
```

**Validation Checklist:**

1. **Application Startup:**
   - [ ] Application loads in browser without errors
   - [ ] No console errors on initial load
   - [ ] UI renders correctly

2. **Primary Workflows:**
   - [ ] Load SARIF file successfully
   - [ ] View analysis results
   - [ ] Filter and sort issues
   - [ ] Extract code snippets
   - [ ] Navigate between issues
   - [ ] Open full details dialog

3. **JS Interop:**
   - [ ] File picker works (directory selection)
   - [ ] File reading works (code snippet extraction)
   - [ ] No JS errors in console

4. **URI/HTTP Functionality:**
   - [ ] File path URIs display correctly
   - [ ] Any HTTP requests succeed (if applicable)
   - [ ] No URI parsing errors

5. **JSON Serialization:**
   - [ ] SARIF file parsing succeeds
   - [ ] No JSON deserialization errors

**If Issues Found:**
- Check browser console for errors
- Review behavioral changes in §Breaking Changes Catalog
- Test in multiple browsers if issues appear browser-specific

---

### Step 10: Final Verification

**Build Clean:**
```bash
dotnet clean Sarifintown.sln
dotnet build Sarifintown.sln --configuration Release
```

**Expected Outcome:**
- Release build succeeds
- 0 errors, minimal warnings

**Checklist:**
- [ ] Both projects target net10.0
- [ ] All 4 packages updated to 10.0.3
- [ ] Solution builds (Debug and Release) with 0 errors
- [ ] All tests pass
- [ ] Application runs and primary features work
- [ ] No console errors or runtime exceptions
- [ ] Behavioral changes validated
- [ ] Ready for commit

---

### Execution Order Summary

1. ✅ Verify Prerequisites (.NET 10 SDK, current build success)
2. ✅ Update both project files to net10.0
3. ✅ Update 4 package references to 10.0.3
4. ✅ Restore dependencies
5. ✅ Build solution (expect 2 errors)
6. ✅ Fix Path.Combine usage
7. ✅ Fix ConfigurationBinder.GetValue usage
8. ✅ Rebuild solution (expect 0 errors)
9. ✅ Run all tests (expect all pass)
10. ✅ Manual application validation
11. ✅ Final verification and commit

---

## Project-by-Project Migration Plans

### Project: Sarifintown\Sarifintown.csproj

#### Current State

- **Target Framework:** net9.0
- **Project Type:** AspNetCore (Blazor WebAssembly)
- **SDK Style:** True
- **Lines of Code:** 1,292
- **Files:** 66
- **Files with Incidents:** 4
- **Dependencies:** 0 project dependencies
- **Dependants:** 1 (Sarifintown.Tests.csproj)
- **Risk Level:** Low

**Current Packages:**
- Markdig 0.41.3
- Microsoft.AspNetCore.Components.WebAssembly 9.0.8
- Microsoft.AspNetCore.Components.WebAssembly.DevServer 9.0.8
- Microsoft.Extensions.Http 9.0.8
- Microsoft.SemanticKernel.Connectors.OpenAI 1.61.0
- MudBlazor 8.11.0
- System.Text.Json 9.0.8

#### Target State

- **Target Framework:** net10.0
- **Updated Packages:** 4 packages require version updates

#### Migration Steps

##### 1. Prerequisites

- Verify .NET 10 SDK installed and available
- Ensure current codebase builds successfully on net9.0
- Review .NET 10 breaking changes documentation:
  - System.Uri behavioral changes
  - HttpClient initialization changes
  - Blazor WebAssembly runtime updates

##### 2. Update Project File

**File:** `Sarifintown\Sarifintown.csproj`

**Change:**
```xml
<TargetFramework>net10.0</TargetFramework>
```

##### 3. Update Package References

| Package | Current Version | Target Version | Reason |
|---------|----------------|----------------|---------|
| Microsoft.AspNetCore.Components.WebAssembly | 9.0.8 | 10.0.3 | Framework alignment |
| Microsoft.AspNetCore.Components.WebAssembly.DevServer | 9.0.8 | 10.0.3 | Framework alignment |
| Microsoft.Extensions.Http | 9.0.8 | 10.0.3 | Framework alignment |
| System.Text.Json | 9.0.8 | 10.0.3 | Framework alignment |

**No Changes Required:**
- Markdig 0.41.3 (compatible)
- Microsoft.SemanticKernel.Connectors.OpenAI 1.61.0 (compatible)
- MudBlazor 8.11.0 (compatible)

##### 4. Expected Breaking Changes

**API Behavioral Changes (9 occurrences):**

1. **System.Uri (5 occurrences)**
   - **Impact:** URI parsing, validation, and comparison behavior may differ
   - **Affected Areas:** Code constructing or manipulating URIs
   - **Action:** Test all URI-related functionality; review .NET 10 URI changes documentation

2. **System.Uri Constructor (3 occurrences)**
   - **Impact:** URI construction validation may be stricter or more lenient
   - **Affected Areas:** Code instantiating Uri objects
   - **Action:** Verify URI string formats comply with .NET 10 parsing rules

3. **HttpClientFactoryServiceCollectionExtensions.AddHttpClient (1 occurrence)**
   - **Impact:** HttpClient registration behavior may change
   - **Affected Areas:** Dependency injection configuration in Program.cs
   - **Action:** Test HTTP client instantiation and request behavior

**Note:** No binary or source incompatible APIs in this project.

##### 5. Code Modifications

**Expected Changes:**
- **Estimated LOC:** 9+ lines requiring modification (0.7% of project)
- **Files Affected:** 4 files with identified incidents

**Areas Requiring Review:**
1. **URI Construction/Parsing**
   - Verify URI string formats and parsing behavior
   - Test URI comparison and equality operations
   - Validate relative URI resolution

2. **HTTP Client Configuration**
   - Review `AddHttpClient` registration in Program.cs/service configuration
   - Test HTTP request creation and execution
   - Verify dependency injection resolves HttpClient correctly

3. **Blazor WebAssembly Hosting**
   - Review `Program.cs` for any .NET 10 hosting model changes
   - Verify static asset serving and routing
   - Test JS interop functionality

4. **JSON Serialization**
   - With System.Text.Json upgrade to 10.0.3, verify serialization behavior
   - Test any custom JsonSerializerOptions configurations

##### 6. Testing Strategy

**Unit Testing:**
- Tests handled by Sarifintown.Tests.csproj (see separate project plan)

**Manual Validation:**
- Run Blazor WebAssembly application in browser
- Verify application loads without errors
- Test primary user flows:
  - SARIF file loading and parsing
  - Analysis visualization
  - Code snippet extraction
  - File path formatting
- Verify JS interop for file system access
- Check console for runtime errors or warnings

**Focus Areas:**
- URI parsing in file path handling
- HTTP requests (if any external API calls)
- JSON serialization/deserialization of SARIF files
- MudBlazor component rendering

##### 7. Validation Checklist

- [ ] Project file updated to net10.0
- [ ] All 4 package references updated to 10.0.3
- [ ] Dependencies restored successfully (`dotnet restore`)
- [ ] Project builds without errors (`dotnet build`)
- [ ] Project builds without warnings
- [ ] Application runs in browser
- [ ] No console errors on startup
- [ ] Primary features functional (SARIF loading, analysis display)
- [ ] No runtime exceptions in normal workflows
- [ ] JS interop functionality works

---

### Project: Sarifintown.Tests\Sarifintown.Tests.csproj

#### Current State

- **Target Framework:** net9.0
- **Project Type:** DotNetCoreApp (Test Project)
- **SDK Style:** True
- **Lines of Code:** 349
- **Files:** 8
- **Files with Incidents:** 2
- **Dependencies:** 1 project dependency (Sarifintown.csproj)
- **Dependants:** 0
- **Risk Level:** Low

**Current Packages:**
- bunit 2.*
- coverlet.collector 6.*
- FluentAssertions 8.*
- Microsoft.NET.Test.Sdk 17.*
- Microsoft.Playwright.NUnit 1.*
- NUnit 4.*
- NUnit.Analyzers 4.*
- NUnit3TestAdapter 4.*

#### Target State

- **Target Framework:** net10.0
- **Package Updates:** None required (all packages compatible)

#### Migration Steps

##### 1. Prerequisites

- Sarifintown.csproj must be upgraded to net10.0 first (dependency requirement)
- Sarifintown.csproj must build successfully before running tests

##### 2. Update Project File

**File:** `Sarifintown.Tests\Sarifintown.Tests.csproj`

**Change:**
```xml
<TargetFramework>net10.0</TargetFramework>
```

##### 3. Package References

**No Updates Required:**

All test packages are compatible with net10.0:
- bunit 2.* (compatible)
- coverlet.collector 6.* (compatible)
- FluentAssertions 8.* (compatible)
- Microsoft.NET.Test.Sdk 17.* (compatible)
- Microsoft.Playwright.NUnit 1.* (compatible)
- NUnit 4.* (compatible)
- NUnit.Analyzers 4.* (compatible)
- NUnit3TestAdapter 4.* (compatible)

##### 4. Expected Breaking Changes

**Binary Incompatible API (1 occurrence):**

1. **ConfigurationBinder.GetValue<T>(IConfiguration, string)**
   - **Impact:** High - Requires code changes
   - **Location:** 1 occurrence in test code
   - **Action:** 
     - Identify usage in test files
     - Review .NET 10 ConfigurationBinder API changes
     - Replace with compatible API or updated overload
     - Verify configuration mocking/setup still works

**Source Incompatible API (1 occurrence):**

2. **Path.Combine(ReadOnlySpan<string>)**
   - **Impact:** Medium - Will not compile
   - **Location:** 1 occurrence in test code
   - **Action:**
     - Identify usage in test files
     - Replace with compatible overload: `Path.Combine(string[])` or expand parameters
     - Example fix:
       ```csharp
       // Before (incompatible):
       Path.Combine(spanOfStrings);

       // After (compatible):
       Path.Combine(spanOfStrings.ToArray());
       // OR
       Path.Combine(path1, path2, path3);
       ```

**Behavioral Changes:**
- None identified for test project

##### 5. Code Modifications

**Expected Changes:**
- **Estimated LOC:** 2+ lines requiring modification (0.6% of test project)
- **Files Affected:** 2 files with identified incidents

**Specific Fixes Required:**

1. **Find ConfigurationBinder.GetValue usage:**
   ```bash
   # Search for usage
   grep -r "GetValue" Sarifintown.Tests/
   ```
   - Review .NET 10 migration guide for ConfigurationBinder
   - Apply recommended replacement API
   - Update test assertions if method signature changed

2. **Find Path.Combine(ReadOnlySpan) usage:**
   ```bash
   # Search for Path.Combine usage
   grep -r "Path.Combine" Sarifintown.Tests/
   ```
   - Replace with compatible overload
   - Ensure test logic remains unchanged

##### 6. Testing Strategy

**Test Execution:**
- Run full test suite after Sarifintown.csproj successfully builds
- Verify all tests pass (NUnit + Playwright tests)
- Check for new test failures caused by framework changes

**Test Categories:**
- **Unit Tests:** bunit component tests
- **UI Tests:** Playwright browser automation tests
- **Integration Tests:** Any tests exercising main application

**Validation Focus:**
- Configuration-related tests (ConfigurationBinder fix)
- File path handling tests (Path.Combine fix)
- Component rendering tests (Blazor components)
- Browser interaction tests (Playwright)

##### 7. Validation Checklist

- [ ] Project file updated to net10.0
- [ ] Dependencies restored successfully
- [ ] Project builds without errors
- [ ] Project builds without warnings
- [ ] ConfigurationBinder.GetValue usage fixed and compiles
- [ ] Path.Combine usage fixed and compiles
- [ ] All unit tests pass
- [ ] All Playwright tests pass
- [ ] No new test failures introduced
- [ ] Test coverage maintained
- [ ] All test projects reference updated Sarifintown.csproj correctly

---

## Package Update Reference

### Summary

- **Total Packages:** 15
- **Packages Requiring Updates:** 4 (26.7%)
- **Packages Remaining Compatible:** 11 (73.3%)

### Updates by Project

#### Sarifintown.csproj (4 updates)

| Package | Current | Target | Update Reason | Priority |
|---------|---------|--------|---------------|----------|
| Microsoft.AspNetCore.Components.WebAssembly | 9.0.8 | 10.0.3 | Framework alignment with .NET 10 | Required |
| Microsoft.AspNetCore.Components.WebAssembly.DevServer | 9.0.8 | 10.0.3 | Framework alignment with .NET 10 | Required |
| Microsoft.Extensions.Http | 9.0.8 | 10.0.3 | Framework alignment with .NET 10 | Required |
| System.Text.Json | 9.0.8 | 10.0.3 | Framework alignment with .NET 10 | Required |

#### Sarifintown.Tests.csproj (0 updates)

No package updates required - all test packages compatible with net10.0.

### Compatible Packages (No Updates Needed)

These packages remain on current versions:

**Sarifintown.csproj:**
- Markdig 0.41.3
- Microsoft.SemanticKernel.Connectors.OpenAI 1.61.0
- MudBlazor 8.11.0

**Sarifintown.Tests.csproj:**
- bunit 2.*
- coverlet.collector 6.*
- FluentAssertions 8.*
- Microsoft.NET.Test.Sdk 17.*
- Microsoft.Playwright.NUnit 1.*
- NUnit 4.*
- NUnit.Analyzers 4.*
- NUnit3TestAdapter 4.*

### Update Strategy

**All-At-Once Package Updates:**
All 4 package updates will be applied simultaneously as part of the atomic upgrade operation:

1. Update all PackageReference elements to target versions
2. Run `dotnet restore` to fetch new packages
3. Build solution to identify any compilation issues
4. Address breaking changes if any surface

### Version Alignment Notes

- All Microsoft framework packages move to 10.0.3 (latest .NET 10 patch at time of assessment)
- MudBlazor 8.11.0 officially supports .NET 10 without version change
- Test framework packages (NUnit, bunit, Playwright) are framework-agnostic
- No conflicting version constraints identified

---

## Breaking Changes Catalog

### Overview

| Category | Count | Severity |
|----------|-------|----------|
| Binary Incompatible | 1 | High |
| Source Incompatible | 1 | Medium |
| Behavioral Change | 9 | Low |
| **Total** | **11** | |

### Critical Breaking Changes

#### 1. Binary Incompatible: ConfigurationBinder.GetValue

**API:** `Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue<T>(IConfiguration, string)`

**Impact:** High - Requires code changes

**Project:** Sarifintown.Tests.csproj

**Occurrences:** 1

**Description:**
The ConfigurationBinder.GetValue method signature or behavior has changed in a binary-incompatible way between .NET 9 and .NET 10.

**Resolution:**
1. Locate usage in test code
2. Review .NET 10 ConfigurationBinder API documentation
3. Options:
   - Use updated method signature/overload
   - Replace with alternative configuration binding API
   - Use IConfiguration indexer directly if applicable

**Example (Hypothetical):**
```csharp
// Potential before (may vary based on actual change):
var value = config.GetValue<string>("key");

// Potential after (consult .NET 10 docs):
var value = config.GetValue<string>("key", defaultValue: null);
// OR
var value = config["key"];
```

**Verification:**
- Build project after fix
- Run tests to ensure configuration mocking still works
- Verify test assertions remain valid

---

#### 2. Source Incompatible: Path.Combine(ReadOnlySpan<string>)

**API:** `System.IO.Path.Combine(ReadOnlySpan<string>)`

**Impact:** Medium - Will not compile

**Project:** Sarifintown.Tests.csproj

**Occurrences:** 1

**Description:**
The Path.Combine overload accepting ReadOnlySpan<string> is not available or has incompatible signature in .NET 10.

**Resolution:**
Replace with compatible overload:
- Use `Path.Combine(string[])` by converting span to array
- Use explicit parameter expansion if path count is known
- Use string concatenation with Path.DirectorySeparatorChar if appropriate

**Example Fix:**
```csharp
// Before (incompatible):
ReadOnlySpan<string> pathSegments = ...;
var combined = Path.Combine(pathSegments);

// After Option 1 (array conversion):
var combined = Path.Combine(pathSegments.ToArray());

// After Option 2 (explicit parameters, if count known):
var combined = Path.Combine(segment1, segment2, segment3);

// After Option 3 (manual concatenation):
var combined = string.Join(Path.DirectorySeparatorChar.ToString(), pathSegments.ToArray());
```

**Verification:**
- Build project after fix
- Run tests exercising path combination logic
- Verify resulting paths are correct

---

### Behavioral Changes

These APIs compile and run but may behave differently at runtime. Thorough testing required.

#### 3. System.Uri Type Behavioral Changes

**API:** `System.Uri` (Type-level)

**Impact:** Low - Behavioral differences

**Project:** Sarifintown.csproj

**Occurrences:** 5

**Description:**
URI parsing, validation, comparison, or string representation may differ in .NET 10.

**Affected Scenarios:**
- URI parsing from strings
- URI validation rules
- URI equality comparisons
- URI.ToString() formatting
- Relative URI resolution

**Testing Required:**
- Verify all URI construction succeeds with expected inputs
- Test URI comparison logic (equality, equivalence)
- Validate URI string representations match expectations
- Check relative URI handling if applicable

**No Code Changes Expected** - but runtime behavior validation is critical.

---

#### 4. System.Uri Constructor Behavioral Changes

**API:** `System.Uri(string)` Constructor

**Impact:** Low - Behavioral differences

**Project:** Sarifintown.csproj

**Occurrences:** 3

**Description:**
URI string parsing when constructing Uri objects may be stricter or more lenient in .NET 10.

**Testing Required:**
- Verify all URI strings parse successfully
- Check for new ArgumentException or UriFormatException cases
- Validate constructed URIs have expected components (scheme, host, path, etc.)

**No Code Changes Expected** - but validation of URI inputs recommended.

---

#### 5. HttpClientFactoryServiceCollectionExtensions.AddHttpClient Behavioral Change

**API:** `Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions.AddHttpClient(IServiceCollection)`

**Impact:** Low - Behavioral differences

**Project:** Sarifintown.csproj

**Occurrences:** 1

**Description:**
HttpClient registration or factory behavior may differ in .NET 10.

**Affected Areas:**
- Service registration in Program.cs or Startup
- HttpClient dependency injection resolution
- HttpClient lifetime management
- HttpClient configuration and handlers

**Testing Required:**
- Verify HttpClient resolves correctly from DI container
- Test HTTP requests execute successfully
- Check HttpClient lifetime behavior (e.g., disposal, pooling)
- Validate any custom HttpMessageHandlers still apply

**No Code Changes Expected** - but HTTP functionality testing critical.

---

### Framework-Specific Considerations

#### Blazor WebAssembly (.NET 10)

While not specific breaking changes, be aware of:

1. **Runtime Updates:**
   - WebAssembly runtime may have performance or behavior changes
   - JS interop mechanisms may differ subtly

2. **Hosting Model:**
   - Check Program.cs hosting configuration
   - Verify static asset serving
   - Validate routing behavior

3. **Browser Compatibility:**
   - .NET 10 WASM may require newer browser versions
   - Test in target browser matrix

4. **AOT Compilation:**
   - If using AOT, rebuild and test thoroughly
   - AOT behavior may change between versions

**Testing Required:**
- Application startup and initialization
- JS interop calls (especially file system access in Sarifintown)
- Component rendering and lifecycle
- Routing and navigation
- Static asset loading

---

### Testing Focus Areas

Based on breaking changes, prioritize testing:

1. **Configuration Usage** (Binary incompatible API)
   - Test configuration loading in test suite
   - Verify mocked configuration scenarios

2. **Path Handling** (Source incompatible API)
   - Test file path combination logic
   - Verify path normalization

3. **URI Operations** (Behavioral changes)
   - Test all URI parsing and construction
   - Verify URI comparisons
   - Check HTTP client functionality

4. **HTTP Requests** (Behavioral change)
   - Test HttpClient DI resolution
   - Execute real or mocked HTTP requests
   - Verify request/response handling

5. **Blazor Components** (Framework upgrade)
   - Test component rendering
   - Verify JS interop
   - Check application routing

---

## Testing & Validation Strategy

### Overview

Multi-level testing approach to validate upgrade across automated tests, manual workflows, and behavioral change verification.

---

### Level 1: Automated Testing

**Scope:** Sarifintown.Tests.csproj test suite

**Execution:**
```bash
dotnet test Sarifintown.sln --logger "console;verbosity=detailed"
```

**Test Categories:**

1. **Unit Tests (bunit)**
   - Blazor component rendering
   - Component interaction logic
   - Component state management
   - Isolated component functionality

2. **UI Tests (Playwright + NUnit)**
   - Browser automation tests
   - End-to-end user workflows
   - Cross-browser compatibility
   - Real DOM manipulation

3. **Integration Tests**
   - Component integration
   - Service interaction
   - Data flow validation

**Success Criteria:**
- All tests pass (0 failed, 0 skipped)
- No new test failures introduced
- Test execution time comparable to pre-upgrade
- No test infrastructure errors

**Failure Response:**
- Categorize failures: framework-related vs application-related
- Fix application code if regression detected
- Update test assertions if .NET 10 behavior legitimately changed
- Document behavioral changes affecting tests

---

### Level 2: Compilation & Build Validation

**Per-Project Build Validation:**

After compilation error fixes applied:

1. **Sarifintown.csproj:**
   ```bash
   dotnet build Sarifintown\Sarifintown.csproj --configuration Debug
   dotnet build Sarifintown\Sarifintown.csproj --configuration Release
   ```
   - Expected: 0 errors
   - Warnings: Review individually, address critical warnings

2. **Sarifintown.Tests.csproj:**
   ```bash
   dotnet build Sarifintown.Tests\Sarifintown.Tests.csproj --configuration Debug
   dotnet build Sarifintown.Tests\Sarifintown.Tests.csproj --configuration Release
   ```
   - Expected: 0 errors
   - Ensure test project builds after main project

**Full Solution Build:**
```bash
dotnet build Sarifintown.sln --configuration Release
```

**Success Criteria:**
- Both projects build without errors
- Minimal warnings
- No package restore conflicts
- Dependency graph resolved correctly

---

### Level 3: Manual Application Validation

**Environment:**
- Run application locally in development mode
- Test in supported browsers (Chrome, Edge, Firefox)

**Primary Workflows:**

#### Workflow 1: SARIF File Loading
1. Launch application
2. Select SARIF file(s) using file picker
3. Verify files load without errors
4. Check JSON deserialization succeeds
5. Validate analysis results display

**Expected:** Files load successfully, no console errors

#### Workflow 2: Analysis Visualization
1. View analysis results grid
2. Filter by severity (High/Medium/Low)
3. Filter by rule
4. Group by file path
5. Sort by different columns

**Expected:** All filtering/sorting/grouping works correctly

#### Workflow 3: Code Snippet Extraction
1. Click "Add Code Snippets" button
2. Grant directory access via file picker
3. Verify snippets extracted from source files
4. Check syntax highlighting renders
5. Validate line numbers correct

**Expected:** Snippets extract correctly, JS interop works

#### Workflow 4: Issue Details
1. Click "Details" on an issue
2. View full details dialog
3. Check all issue metadata displays
4. Verify paths formatted correctly
5. Close dialog

**Expected:** Details display correctly, no rendering errors

**Validation Checklist:**
- [ ] Application starts without errors
- [ ] UI renders correctly
- [ ] SARIF file parsing succeeds
- [ ] File picker JS interop works
- [ ] Code snippet extraction works
- [ ] Syntax highlighting renders
- [ ] Navigation and routing work
- [ ] Dialogs open/close correctly
- [ ] No console errors
- [ ] No runtime exceptions

---

### Level 4: Behavioral Change Verification

**Focus Areas from Breaking Changes:**

#### System.Uri Behavioral Changes

**Test Scenarios:**
1. **URI Parsing:**
   - Verify file path URIs construct correctly
   - Test URI string format expectations
   - Validate URI parsing edge cases

2. **URI Comparison:**
   - Check any URI equality comparisons
   - Verify URI equivalence logic
   - Test case sensitivity handling

3. **URI String Representation:**
   - Validate URI.ToString() output
   - Check formatted path display
   - Verify relative URI handling

**Validation:**
- File paths display correctly in UI
- No URI parsing exceptions
- Path comparison logic works as expected

#### HttpClient Behavioral Changes

**Test Scenarios:**
1. **HttpClient Registration:**
   - Verify DI configuration in Program.cs
   - Check HttpClient resolves from container
   - Test HttpClient lifetime management

2. **HTTP Requests:**
   - If application makes HTTP requests, test them
   - Verify request/response handling
   - Check error handling

**Validation:**
- HttpClient instantiates correctly
- No HTTP-related errors
- Request handling unchanged

#### Configuration Behavioral Changes

**Test Scenarios:**
- If application uses configuration, verify loading
- Check ConfigurationBinder usage (after fix applied)
- Test configuration value retrieval

**Validation:**
- Configuration loads successfully
- No configuration-related errors

---

### Level 5: Regression Testing

**Pre-Upgrade Baseline:**
Before upgrade, document:
- Application startup time
- SARIF file loading performance
- Test suite execution time
- Memory usage patterns (if measurable)

**Post-Upgrade Comparison:**
After upgrade, compare:
- Application startup time (should be comparable or faster)
- SARIF file loading performance (should be similar)
- Test suite execution time (should be similar)
- Memory usage (should be similar or better)

**Success Criteria:**
- No significant performance regressions (>20% slower)
- No memory usage increases (>20% more)
- Application responsiveness maintained

---

### Testing Timeline

**During Upgrade:**
1. **After Build Fixes:** Compile-time validation (Level 2)
2. **After Compilation:** Automated test suite (Level 1)
3. **After Tests Pass:** Manual validation (Level 3)
4. **After Manual Testing:** Behavioral verification (Level 4)
5. **Before Commit:** Regression check (Level 5)

**Post-Upgrade:**
- Run full test suite regularly
- Monitor application in production (if applicable)
- Watch for user-reported issues related to upgrade

---

### Test Failure Decision Tree

```
Test Failure Detected
    │
    ├─→ Compilation Error?
    │   ├─→ API Incompatibility → Fix code per Breaking Changes Catalog
    │   └─→ Syntax Error → Fix code syntax
    │
    ├─→ Test Failure?
    │   ├─→ Framework Behavioral Change → Update test assertions
    │   └─→ Application Regression → Fix application code
    │
    ├─→ Runtime Error?
    │   ├─→ API Behavioral Change → Update application code
    │   └─→ Unexpected Exception → Debug and fix
    │
    └─→ Performance Regression?
        ├─→ Significant (>20%) → Investigate .NET 10 change, consider optimization
        └─→ Minor (<20%) → Monitor, may be acceptable
```

---

## Source Control Strategy

### Branching Strategy

**Current Branch:** db_upgrade

**Upgrade Execution:** All changes applied directly on current branch

**Branch Structure:**
- No new branch creation required
- All upgrade work happens on `db_upgrade`
- Single atomic commit when upgrade complete

**Rationale:**
- User explicitly requested using current branch
- Simplifies workflow for small upgrade
- Single commit enables easy rollback if needed

---

### Commit Strategy

**Single Atomic Commit (Recommended)**

All upgrade changes committed together in one comprehensive commit:

**Commit Message Template:**
```
Upgrade solution to .NET 10.0

- Update both projects from net9.0 to net10.0
- Update 4 Microsoft packages to version 10.0.3:
  - Microsoft.AspNetCore.Components.WebAssembly
  - Microsoft.AspNetCore.Components.WebAssembly.DevServer
  - Microsoft.Extensions.Http
  - System.Text.Json
- Fix Path.Combine(ReadOnlySpan) source incompatibility in tests
- Fix ConfigurationBinder.GetValue binary incompatibility in tests
- All tests passing
- Application validated manually

Breaking changes addressed:
- Path.Combine overload replaced with compatible version
- ConfigurationBinder.GetValue usage updated for .NET 10
- System.Uri behavioral changes validated
- HttpClient behavioral changes tested

Projects upgraded:
- Sarifintown\Sarifintown.csproj
- Sarifintown.Tests\Sarifintown.Tests.csproj
```

**Commit Contents:**
- 2 project file modifications
- 2 test code files (breaking change fixes)
- Any other code files modified for behavioral changes

**Commit Execution:**
```bash
git add -A
git commit -m "Upgrade solution to .NET 10.0

[Include detailed message from template]"
```

---

### Alternative: Phased Commits (Not Recommended for All-At-Once)

If absolutely necessary to split commits:

**Commit 1: Framework and Package Updates**
```
Update framework to .NET 10.0 and package versions

- Update TargetFramework to net10.0 in both projects
- Update 4 Microsoft packages to 10.0.3
- Expected: Build failures due to API incompatibilities
```

**Commit 2: Breaking Change Fixes**
```
Fix .NET 10 API incompatibilities

- Fix Path.Combine usage in tests
- Fix ConfigurationBinder.GetValue usage in tests
- All compilation errors resolved
- All tests passing
```

**Note:** Phased commits create intermediate broken states, violating All-At-Once principle. Only use if required by team policy.

---

### Review and Merge Process

**Pre-Commit Checklist:**
- [ ] Both projects target net10.0
- [ ] All 4 packages updated to 10.0.3
- [ ] Solution builds with 0 errors (Debug and Release)
- [ ] All tests pass
- [ ] Application runs and primary features validated
- [ ] No console errors or runtime exceptions
- [ ] Behavioral changes tested and documented
- [ ] Commit message complete and accurate

**Code Review Considerations:**
- Review project file changes (TargetFramework, PackageReference)
- Review breaking change fixes (Path.Combine, ConfigurationBinder.GetValue)
- Verify test results (all passing)
- Check for any unintended code changes

**Merge Strategy:**
- No merge required if working directly on current branch
- If branch created, merge back to main branch after validation

---

### Rollback Strategy

**If Issues Discovered Post-Commit:**

**Option 1: Revert Commit**
```bash
git revert <commit-hash>
```
Creates new commit that undoes upgrade changes.

**Option 2: Hard Reset (If Not Pushed)**
```bash
git reset --hard HEAD~1
```
Removes commit entirely from branch history.

**Option 3: Create Fix Commit**
If issue is minor, create new commit with fix rather than reverting entire upgrade.

**Rollback Decision Criteria:**
- **Revert if:** Blocking production issue, major regressions, critical bugs
- **Fix forward if:** Minor issues, cosmetic problems, non-blocking bugs
- **Research if:** Uncertain if issue relates to upgrade or pre-existing

---

## Success Criteria

### Technical Criteria

#### 1. Framework Upgrade
- [ ] Sarifintown.csproj targets net10.0
- [ ] Sarifintown.Tests.csproj targets net10.0
- [ ] No remaining net9.0 references in project files

#### 2. Package Updates
- [ ] Microsoft.AspNetCore.Components.WebAssembly updated to 10.0.3
- [ ] Microsoft.AspNetCore.Components.WebAssembly.DevServer updated to 10.0.3
- [ ] Microsoft.Extensions.Http updated to 10.0.3
- [ ] System.Text.Json updated to 10.0.3
- [ ] All other packages remain on compatible versions
- [ ] No package version conflicts

#### 3. Build Success
- [ ] `dotnet build Sarifintown.sln --configuration Debug` succeeds with 0 errors
- [ ] `dotnet build Sarifintown.sln --configuration Release` succeeds with 0 errors
- [ ] `dotnet restore` completes without errors
- [ ] No critical warnings introduced

#### 4. Test Success
- [ ] `dotnet test Sarifintown.sln` passes with 0 failures
- [ ] All unit tests pass
- [ ] All integration tests pass
- [ ] All Playwright UI tests pass
- [ ] No tests skipped (unless intentional)

#### 5. Breaking Changes Addressed
- [ ] Path.Combine(ReadOnlySpan) usage fixed
- [ ] ConfigurationBinder.GetValue usage fixed
- [ ] System.Uri behavioral changes validated
- [ ] HttpClient behavioral changes tested
- [ ] No compilation errors from API incompatibilities

#### 6. Application Functionality
- [ ] Application starts without errors
- [ ] SARIF file loading works
- [ ] Analysis visualization renders
- [ ] Code snippet extraction works
- [ ] JS interop functionality operational
- [ ] No console errors during normal usage
- [ ] No runtime exceptions in primary workflows

---

### Quality Criteria

#### 1. Code Quality Maintained
- [ ] No code quality regressions introduced
- [ ] Breaking change fixes use idiomatic .NET 10 patterns
- [ ] No temporary workarounds or hacks
- [ ] Code readability maintained or improved

#### 2. Test Coverage Maintained
- [ ] Test coverage percentage unchanged or improved
- [ ] No tests removed to make upgrade work
- [ ] Test assertions remain meaningful
- [ ] New tests added for behavioral changes (if applicable)

#### 3. Documentation Updated
- [ ] README.md updated if .NET version mentioned
- [ ] Any documentation referencing .NET 9 updated to .NET 10
- [ ] Breaking changes documented in commit message
- [ ] Known issues or behavioral changes documented (if any)

#### 4. No Regressions
- [ ] Application performance comparable to pre-upgrade
- [ ] Memory usage comparable to pre-upgrade
- [ ] No functional regressions in existing features
- [ ] User workflows unaffected by upgrade

---

### Process Criteria

#### 1. All-At-Once Strategy Followed
- [ ] Both projects upgraded simultaneously
- [ ] All packages updated in single operation
- [ ] No intermediate multi-targeting states
- [ ] Single atomic commit created (or approved phased commits)

#### 2. Source Control Best Practices
- [ ] Commit message complete and descriptive
- [ ] All changes included in commit
- [ ] No unrelated changes mixed into upgrade commit
- [ ] Branch strategy followed

#### 3. Validation Complete
- [ ] All testing levels executed (automated, manual, behavioral)
- [ ] Regression testing performed
- [ ] Pre-commit checklist completed
- [ ] Code review performed (if applicable)

---

### Definition of Done

**The .NET 10 upgrade is complete when:**

1. ✅ All **Technical Criteria** met
2. ✅ All **Quality Criteria** met
3. ✅ All **Process Criteria** met
4. ✅ Single atomic commit created and pushed
5. ✅ Documentation updated
6. ✅ Team/stakeholders notified of upgrade completion

**At this point:**
- Solution fully upgraded to .NET 10
- All functionality validated
- No known regressions
- Ready for continued development on .NET 10

---

### Post-Upgrade Monitoring

**After declaring success, monitor for:**
- User-reported issues potentially related to upgrade
- Performance metrics in production (if applicable)
- Any edge cases not covered by testing
- Compatibility issues with external integrations

**If issues discovered:**
- Assess severity and impact
- Determine if upgrade-related or pre-existing
- Create fix or workaround as appropriate
- Update testing to catch similar issues in future

---

## Risk Management

### High-Level Risk Assessment

**Overall Risk: Low**

Both projects rated as "Low" difficulty with minimal code changes required.

### Risk Categories

| Risk Category | Level | Description | Mitigation |
|--------------|-------|-------------|------------|
| **Framework Compatibility** | Low | Single version jump (net9.0 → net10.0) | Well-documented migration path |
| **Package Compatibility** | Low | 4 packages require updates, all have clear target versions | All packages from Microsoft with official .NET 10 support |
| **API Breaking Changes** | Medium | 1 binary incompatible, 1 source incompatible | Specific fix guidance available; limited code surface affected |
| **Behavioral Changes** | Low | 9 API behavioral changes (mainly System.Uri) | Comprehensive testing to detect runtime impacts |
| **Build System** | Low | SDK-style projects with straightforward upgrade | Minimal project file modifications required |
| **Testing** | Low | Dedicated test project with existing coverage | Tests validate changes immediately after upgrade |
| **Rollback** | Low | Single atomic commit | Easy to revert if issues discovered |

### Specific Risk Items

#### 1. Binary Incompatible API Change

**Risk:** `ConfigurationBinder.GetValue<T>(IConfiguration, string)` is binary incompatible

**Impact:** Medium - Code using this API will fail at runtime if not recompiled and may require signature changes

**Mitigation:**
- Identify all usages before upgrade
- Review .NET 10 migration documentation for ConfigurationBinder changes
- Test configuration loading scenarios thoroughly
- Consider alternative APIs if signature incompatible

**Affected Files:** Assessment indicates 1 occurrence in codebase

---

#### 2. Source Incompatible API Change

**Risk:** `Path.Combine(ReadOnlySpan<string>)` is source incompatible

**Impact:** Medium - Code using this overload will not compile in .NET 10

**Mitigation:**
- Identify usages before upgrade
- Replace with compatible overload (e.g., `Path.Combine(string[])` or parameter expansion)
- Verify build errors surface this issue clearly

**Affected Files:** Assessment indicates 1 occurrence in codebase

---

#### 3. System.Uri Behavioral Changes

**Risk:** 9 behavioral changes identified related to System.Uri (5 type-level, 3 constructor-level, 1 HttpClient-related)

**Impact:** Low - Existing code compiles but may behave differently at runtime

**Mitigation:**
- Review .NET 10 breaking changes documentation for System.Uri
- Test all HTTP/URI-related functionality
- Focus on URI parsing, validation, and comparison scenarios
- Verify HttpClient instantiation and request behavior

**Affected Areas:**
- URI construction and parsing
- HTTP client initialization

---

#### 4. Blazor WebAssembly Specific Risks

**Risk:** WebAssembly runtime and hosting model changes in .NET 10

**Impact:** Low - Generally backward compatible, but runtime behavior may differ

**Mitigation:**
- Test application startup and initialization
- Verify JS interop functionality
- Check browser compatibility (especially with updated runtime)
- Validate static asset serving and routing

---

### Contingency Plans

#### If Compilation Fails

**Scenario:** Build errors after framework/package updates

**Actions:**
1. Review build output for specific error codes
2. Consult .NET 10 breaking changes documentation
3. Address API incompatibilities using documented alternatives
4. If blocked, revert commit and research specific APIs causing issues

#### If Tests Fail

**Scenario:** Tests fail after successful build

**Actions:**
1. Identify failing test categories (unit vs integration)
2. Isolate failures to framework changes vs application logic
3. Review behavioral changes documentation for affected APIs
4. Update test assertions if expectations changed legitimately
5. Fix application code if regressions detected

#### If Behavioral Changes Cause Runtime Issues

**Scenario:** Application runs but exhibits unexpected behavior

**Actions:**
1. Compare behavior against .NET 10 behavioral change documentation
2. Determine if change is expected or regression
3. Update application code to align with new framework behavior
4. Add tests to prevent future regressions
5. If change unacceptable, explore workarounds or alternatives

#### If Rollback Required

**Scenario:** Blocking issues discovered, need to revert

**Actions:**
1. Revert single atomic commit containing all upgrade changes
2. Document specific blocking issue for research
3. Investigate resolution in isolated branch
4. Retry upgrade once resolution confirmed

---

## Complexity & Effort Assessment

### Overall Complexity: Low

**Assessment Basis:**
- Simple solution structure (2 projects, linear dependency)
- Modern framework baseline (net9.0 → net10.0, single version jump)
- Minimal code impact (11 LOC across 1,641 total)
- Clear package upgrade path (4 packages, all Microsoft-supported)
- Low API compatibility issues (2 critical, 9 behavioral)

### Project Complexity Ratings

| Project | Complexity | Dependencies | Risk | Rationale |
|---------|-----------|--------------|------|-----------|
| **Sarifintown.csproj** | Low | 0 project deps | Low | Blazor WebAssembly app with 4 package updates; 9 behavioral changes but no binary/source incompatibilities in this project |
| **Sarifintown.Tests.csproj** | Low | 1 project dep | Low | Test project with no package updates; 2 API compatibility issues affecting limited test code |

### Phase Complexity

**Single Atomic Phase: Low Complexity**

- **Framework Update:** Straightforward TargetFramework modification in 2 project files
- **Package Updates:** 4 packages with clear version targets (9.0.8 → 10.0.3)
- **Code Changes:** Estimated 11 LOC requiring modification
- **Validation:** Single test project with existing coverage

**Complexity Drivers:**
- ✅ Small codebase (1,641 LOC total)
- ✅ SDK-style projects (simple format)
- ✅ No legacy .NET Framework conversion
- ✅ Well-defined package versions
- ⚠️ 2 critical API incompatibilities (binary + source)
- ⚠️ 9 behavioral changes requiring testing

### Resource Requirements

**Skill Level:**
- Intermediate .NET developer
- Familiarity with Blazor WebAssembly
- Understanding of package management
- Experience with test-driven validation

**Estimated Effort (Relative Complexity):**
- **Project Updates:** Low - Modify 2 project files
- **Package Updates:** Low - Update 4 package references
- **Code Fixes:** Low - ~11 LOC across 6 files
- **Testing:** Low - Run existing test suite
- **Validation:** Low - Verify Blazor WebAssembly functionality

**Note:** Time estimates intentionally omitted - duration depends on team familiarity, testing thoroughness, and issue discovery. Relative complexity ratings indicate this is a straightforward upgrade.

---

## Source Control Strategy

[To be filled]

---

## Success Criteria

[To be filled]
