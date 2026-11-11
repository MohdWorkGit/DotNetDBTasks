# Local AI for Jadwal — Options

*Written 2026-11-10. Versions, model sizes and licences were checked on the web on that date (see
[Sources](#sources)) and will move, so check them again before building. The decisions taken are
in [§11](#11-decisions-2026-11-10); Phase 0 (§10) is being built.*

---

## 1. Summary

**Recommendation:** run **Ollama** as its own Windows service on the Jadwal server, listening on
`127.0.0.1` only. Ship it, with one model, in a **separate offline AI bundle**. Jadwal talks to it
over its OpenAI-compatible HTTP API, so the runtime (Ollama, llama.cpp, Foundry Local) and the model
can be swapped in config without code changes.

The server will have a GPU with 8–16 GB of memory (§11). The default model is **Gemma 4 12B**
(Apache 2.0, about 9.5 GB, best Arabic at this size). Use **gpt-oss 20B** if the card has 16 GB,
or **Qwen3.5 9B** if it has 8 GB.

The AI only ever **drafts**: SQL, a dashboard layout or a report layout. The admin reviews the draft,
previews it, and saves it through the same forms and validators as today. Nothing the model writes
runs or saves by itself.

| | Recommended | Fallback | Not recommended |
|---|---|---|---|
| **Runtime** | Ollama (MIT), separate service | llama.cpp `llama-server.exe` (MIT) | In-process LLamaSharp; vLLM (Linux/GPU only); LM Studio (desktop app) |
| **Model, CPU only** | Qwen3.5 9B | Qwen3 8B, Gemma 4 E4B | anything over 14B on CPU (too slow) |
| **Model, 8–16 GB GPU (the target)** | Gemma 4 12B; gpt-oss 20B on a 16 GB card | Qwen3.5 9B (8 GB card), Devstral Small 2 (24B) | — |
| **Model, 24 GB+ GPU** | Qwen3.8 27B | Gemma 4 31B (best Arabic of the Apache models) | Other licences only after review (Llama community licence, Gemma 3 terms, Laguna OpenMDW) |
| **.NET client** | Plain typed `HttpClient` → `/v1/chat/completions` | Microsoft.Extensions.AI `IChatClient` | A provider-specific SDK |
| **First feature** | Explain / fix SQL | — | End-user "ask the data" (out of scope: admins only) |

---

## 2. What the AI would do in Jadwal

All four features are for admins and authors only, and all four produce a **draft**.

### 2.1 Write SQL from a description
- **Where:** a new "Write with AI" button in the query form
  ([query-form.component.ts](../client/src/app/features/admin/components/query-form/query-form.component.ts)).
- **In:** the chosen connection, the admin's description ("total orders per branch last month"), and
  the relevant tables and columns (see §6).
- **Out:**
  - a single `SELECT` written in Jadwal's own style: `@param` binds and `[bracket]` names.
    `AdaptSqlSyntax` ([QueryExecutor.cs:334](../src/Jadwal.Infrastructure/Services/QueryExecutor.cs#L334))
    already converts that style for each database;
  - suggested `QueryParameter`s (name, display name, type);
  - one line explaining what the query does.
- **Then:** the SQL drops into the editor, and the admin presses the existing **Test** button
  (`TestDynamicQueryCommand`, at most 100 rows) before saving.

### 2.2 Draft a dashboard
- **Where:** the dashboard builder
  ([dashboard-form.component.ts](../client/src/app/features/admin/components/dashboards/dashboard-form.component.ts)).
- **In:** the saved queries the admin picks, plus their column names and types from the existing
  `probe-columns` endpoint
  ([DashboardsController.cs:59](../src/Jadwal.API/Controllers/DashboardsController.cs#L59)),
  plus an optional goal ("a sales overview for managers").
- **Out:** a list of tiles that matches part of `SaveDashboardCommand`
  ([DashboardDtos.cs](../src/Jadwal.Application/Features/Dashboards/Dtos/DashboardDtos.cs)). Each tile has:
  - a visual type (one of the 14 `DashboardVisualType` values);
  - the category and series columns;
  - KPI fields;
  - a width and height.
- **Then:** the tiles load into the form unsaved, and the admin adjusts them and saves. **No SQL is
  written** for this feature, so it is the safest after "Explain".
- **As built (2026-11-11):**
  - **What the model sees:** each picked query (up to 6) runs once, as the column probe does. The
    model is sent only the query's name and description, and for each column its name, kind
    (number, date, text or yes/no) and how many different values it has. **No values are sent.**
  - **What the code fixes or decides:** the model's answer is checked against the real columns.
    Wrong columns are fixed, or the tile is dropped with a note. Sizes are set by code. The
    dashboard filters and parameter mappings are built from the queries' own parameters, never by
    the model.
  - **Required parameters with no default:** a required date parameter starts at `today-365` (the
    first) or `today` (any later one), and a fixed-list dropdown at its first option, so the query
    can run. The draft says so in its notes.
  - **Not offered to the model:** Combo, Heatmap and Gauge tiles, which need settings a draft
    cannot sensibly guess.

### 2.3 Draft a report
- **Where:** the report form
  ([report-form.component.ts](../client/src/app/features/admin/components/reports/report-form.component.ts)).
  Its datasets, parameters and charts are `FormArray`s.
- **In:** the saved queries the admin picks, with their columns and parameters.
- **Out:** datasets (Query / Join / Detail), report parameters, parameter mappings and `ReportChart`
  rows.
- **Then:** the draft loads into the form unsaved. The Word template (`TemplateDocx`) stays the
  admin's job. The AI can list which `{{RESULTS:key}}` / `{{CHART:key}}` markers to place.
- This is the hardest of the four: reports have the most rules (`SaveReportCommand` validation runs
  from line 160 to 310). Do it last.

### 2.4 Explain / fix SQL
- **Explain:** turns a saved query into plain words, in the UI language. This helps auditors and new
  admins.
- **Fix:** when a test run fails, sends the SQL and the database error to the model, which suggests a
  corrected query. The admin sees the old and new SQL side by side.
- **No schema and no data are needed**, so it is the lowest-risk way to prove the setup works.

---

## 3. Runtime options (how the model runs on the server)

| | **Ollama** | **llama.cpp server** | **LLamaSharp (in-process)** | **Foundry Local** |
|---|---|---|---|---|
| Licence | MIT | MIT | MIT | Microsoft licence, check terms |
| Offline install | `ollama-windows-amd64.zip`, no installer needed | A single exe + DLLs (CPU or CUDA build) | NuGet packages + native DLLs | SDK/installer |
| Model files | Copy the models folder (manifests + blobs); `OLLAMA_MODELS` points at it | One `.gguf` file | One `.gguf` file | Its own catalogue (ONNX) |
| Runs as | A separate process; a Windows service via NSSM or a startup task | A separate process, same options | **Inside the IIS worker** | A separate process or in-process |
| Forced JSON output | Yes: JSON schema via `format`, or `response_format` on `/v1` | Yes: JSON schema and GBNF grammars | Yes (grammars) | Yes |
| OpenAI-compatible API | `/v1/chat/completions` | `/v1/chat/completions` | No (C# API) | Yes |
| Model choice | Widest | Any GGUF | Any GGUF | Smaller list |

**Why Ollama:**
- It is the easiest to install with no internet: unzip it, copy a folder.
- It picks CPU or GPU by itself.
- Its model library has all the candidates below.
- It speaks the same API as llama.cpp and Foundry Local, so moving to either later is a config change.

**Why not in-process:**
- Jadwal's IIS pool must run **exactly one worker process**
  ([DEPLOY-AIRGAPPED.md §C3.5](../DEPLOY-AIRGAPPED.md)). Loading 6–20 GB of weights into that
  process means every app-pool recycle reloads the model.
- A crash in native inference code would take the scheduled exports and the long-query worker down
  with it.
- A separate process keeps the API's memory and stability as they are today.

**Must-dos for any runtime:**
- **Bind to `127.0.0.1` only.** Neither Ollama nor llama-server has any login of its own. Anyone who
  can reach the port can use the model.
- **Keep the model loaded.** Set `OLLAMA_KEEP_ALIVE` long, or the first request after an idle period
  waits 10–60 s for the model to load.
- **Run one request at a time on CPU** (`OLLAMA_NUM_PARALLEL=1`). Parallel requests on CPU just make
  every one of them slower.

---

## 4. Model options by hardware

**The target is the 8–16 GB GPU tier** (§11). The GPU's maker and exact memory are not known yet,
so the model is picked at install time from the table below:
- **12–16 GB:** Gemma 4 12B, the default.
- **16 GB, if reasoning matters more than Arabic:** gpt-oss 20B.
- **8 GB:** Qwen3.5 9B.

The CPU rows stay as the fallback if the GPU is late. On CPU, speed depends mostly on **memory
bandwidth**, not core count.

| Tier | Model (Ollama tag family) | Size on disk (Q4) | Memory needed | Licence | Arabic | Notes |
|---|---|---|---|---|---|---|
| **CPU, 16–32 GB RAM** | **Qwen3.5 9B** | ~6.6 GB | 16 GB RAM | Apache 2.0 | Good (201 languages) | Best all-rounder at this size |
| | Qwen3 8B | ~5 GB | 8–16 GB | Apache 2.0 | Good | Lighter and faster, a little weaker |
| | Gemma 4 E4B | ~6.6–9.5 GB | 16 GB | Apache 2.0 | Good | Alternative if Qwen misbehaves |
| **GPU 8–16 GB VRAM** | gpt-oss 20B | ~14 GB | 16 GB | Apache 2.0 | Fair | Strong reasoning; set effort to "low" for speed |
| | Gemma 4 12B | ~9.5 GB | 12–16 GB | Apache 2.0 | Very good | Best Arabic at this size |
| | Devstral Small 2 (24B) | ~15 GB | 16 GB | Apache 2.0 | Fair | Coding specialist |
| **GPU 24 GB+ VRAM** | **Qwen3.8 27B** | ~18 GB | 24 GB | Apache 2.0 | Good | Current best local model for code and SQL |
| | Gemma 4 31B | ~19–20 GB | 24–32 GB | Apache 2.0 | Best (HELM Arabic 0.823) | Pick this if Arabic explanations matter most |

**Rough speed:**
- **On CPU**, an 8–9B model writes about **8–30 tokens (word pieces) per second**. A typical SQL
  answer is 150–400 tokens, so 10–40 s.
- **But first the model has to read the prompt.** On CPU that runs at roughly 50–200 tokens/s, so a
  schema of 4,000 tokens adds 20–80 s.
- **On a GPU**, the same work takes a few seconds.
- **This is why §6 matters:** sending only the relevant tables is the biggest speed lever on CPU.
- **Turn "thinking" off** (Qwen3.x and gpt-oss can reason step by step before answering). It doubles
  or triples the wait for little gain on these tasks. `Ai:DisableThinking` (on by default) sends
  `reasoning_effort: "none"`.
- **Measured on the development PC** (2026-11-10: 4-core i5-6600, 8 GB RAM, no GPU, `qwen3:4b`,
  Ollama 0.23.1):
  - with thinking on, a short explanation took **279 s**; with it off, **22 s**;
  - Explain on a real 9-line query: 82–158 s;
  - Suggest a fix: 37–72 s.

  The answers were usable, but the 4B model also showed why everything is a draft:
  - once it fixed `GROUP` → `GROUP BY` and also misspelt `EMPLOYEES` as `EMPLOYES`. The suggestion
    now highlights every changed word, and the admin must test again before saving;
  - it explained a fix in English on the Arabic page when the database error was in English.

  Judge speed and Arabic on the production GPU and model, not on this PC.

**Allowed only after a licence review** (decision 2, §11). Each needs its licence read before it is
used:
- **Llama 3.x:** the Meta community licence has its own conditions.
- **Gemma 3:** the Gemma terms of use, not Apache. Gemma 4 moved to Apache 2.0, so it rarely needs
  Gemma 3.
- **Laguna:** the OpenMDW licence.
- **Arabic-first models** (Jais 2, Falcon-H1 Arabic): very good Arabic, worth a try if the Arabic
  explanations from Gemma 4 fall short. SQL is still the core job, so test them with the §10 set.
- **Codestral:** a non-production licence, so it stays out.

**Recommendation:** start with an Apache-2.0 model and keep the model name in config. Pick the final
model with the test set in §10, not with benchmarks.

---

## 5. How Jadwal would talk to the model

- **One small interface**, `IAiAssistant`, in `Jadwal.Application`, with methods such as
  `ExplainSqlAsync`, `SuggestSqlAsync`, `DraftDashboardAsync` and `DraftReportAsync`.
- **Implementation:** in `Jadwal.Infrastructure`, a **typed `HttpClient`** that posts to
  `/v1/chat/completions` with `response_format` set to a JSON schema. The model can then only answer
  in the exact shape Jadwal expects.
  - The app has no `HttpClient` registrations today, so this is the first.
  - A plain `HttpClient` adds **no new NuGet packages**. That keeps the vulnerability audit and the
    offline bundle as they are.
  - **Microsoft.Extensions.AI** (`IChatClient`) is the alternative if more AI features come later. It
    is MIT, but it pulls in the OpenAI SDK or OllamaSharp.
- **Config:** a new `Ai` section in `appsettings.json`, read the same way as `ResultCache`
  (`ResultCacheOptions.FromConfiguration`, no `IOptions`):

  ```json
  "Ai": {
    "Enabled": false,
    "BaseUrl": "http://127.0.0.1:11434/v1",
    "Model": "qwen3.5:9b",
    "TimeoutSeconds": 180,
    "MaxSchemaTokens": 3000
  }
  ```

  Add an `ai.enabled` system setting (`SystemSettingKeys`) so an admin can switch AI off from the UI
  without a restart. The client hides every AI button when it is off.
- **Slow answers:**
  - Today's client calls give up after 30 s (`timeout(30000)`, used in 23 places), so AI calls need
    their own, longer timeout.
  - If CPU answers regularly take over about 60 s, move them onto the existing job-and-poll pattern
    (`QueryJobWorker` + polling, as long report runs already do). That avoids long-held HTTP
    requests through IIS.
- **Health check:** at startup and on the System Settings page, call the model's `/v1/models` and
  show "AI: ready / not reachable / model missing".

---

## 6. Giving the model the database structure

Jadwal **does not read table or column lists from the connected databases today.** The only column
discovery runs a whole saved query (`probe-columns`). To write SQL, the model needs the structure, so
this part is new work.

- **A schema reader for each provider:**

  | Database | Read from |
  |---|---|
  | Oracle | `ALL_TAB_COLUMNS` (+ `ALL_CONS_COLUMNS` for keys), filtered to the connection's own schema |
  | SQL Server | `INFORMATION_SCHEMA.COLUMNS` (+ key constraints) |
  | PostgreSQL | `INFORMATION_SCHEMA.COLUMNS` |
  | MySQL | `INFORMATION_SCHEMA.COLUMNS` |

  It runs on the connection's own credentials (decrypted the same way the executor does), so it only
  sees what that account can see. Cache the result per connection, with a "Refresh structure" button.
- **Keeping it short:** real databases have hundreds of tables, and on CPU every extra table costs
  seconds. In order of effort:
  1. **The admin ticks the tables** in the "Write with AI" dialog. This is simple and exact.
     Recommended to start.
  2. **Keyword ranking:** match the description's words against table and column names, and keep
     the top tables within `MaxSchemaTokens`.
  3. **Embeddings** (e.g. `nomic-embed-text`, 0.3 GB, also served by Ollama). Only if 1 and 2 fall
     short.
- **Business descriptions** help more than anything else. Column names like `ORD_STS_CD` mean little
  to a model. A short, optional description per table and column, written once by an admin, makes
  the SQL much better. It also lets Arabic descriptions map to English column names.
- **Sample rows:** a few real values per column also improve the SQL (for example, showing that
  status is `'A'`/`'C'`, not `'Active'`). This is **off by default** and becomes a system setting if
  the user wants it.

---

## 7. Safety and control

The model runs on the server, so **no data leaves the building**. The risks are wrong or harmful SQL,
and the model being used by people who shouldn't use it.

1. **A SELECT-only check for AI SQL.** Today's rule
   ([SqlSafetyRules.cs:20](../src/Jadwal.Application/Common/Security/SqlSafetyRules.cs#L20)) only
   blocks `;`, `--` and identifiers starting `XP_`/`SP_`/`DBMS_`/`UTL_`. The classifier
   ([QueryType.cs:36](../src/Jadwal.Domain/Enums/QueryType.cs#L36)) looks only at the first word, so
   `DROP`, `MERGE` and `ALTER` are saved as "Other" and can run. AI output should also:
   - remove `/* */` comments before checking;
   - allow exactly one statement;
   - require the first word to be `SELECT` or `WITH`;
   - reject `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `DROP`, `ALTER`, `CREATE`, `TRUNCATE`, `GRANT`,
     `EXEC` and `CALL` anywhere outside string literals;
   - stay under `query.sqlMaxLength` (default 4000).

   The normal save validators still run afterwards.
2. **Let CTE queries be tested.** The executor already treats `WITH …` as a query that returns rows
   ([QueryExecutor.cs:396](../src/Jadwal.Infrastructure/Services/QueryExecutor.cs#L396)), but the
   classifier calls it "Other", so the Test button refuses it
   ([TestDynamicQueryCommand.cs:104](../src/Jadwal.Application/Features/DynamicQueries/Commands/TestDynamicQueryCommand.cs#L104)).
   Models write CTEs often, so this needs fixing first. It is a small fix that also helps hand-written
   queries.
3. **Read-only database accounts.** The strongest safety net does not depend on Jadwal at all. Advise
   that connections used with AI log in with accounts that can only `SELECT`.
4. **Permission.** Add a new `ai.use` permission, on top of the existing
   `queries.manage` / `dashboards.manage` / `reports.manage` for each feature. Admin has it by
   default, and other roles get it only when granted.
5. **Connection access.** Every AI call that touches a connection goes through
   `QueryAccess.EnsureCanUseDatabaseUserAsync`
   ([QueryAccess.cs:103](../src/Jadwal.Application/Common/Security/QueryAccess.cs#L103)). Related
   existing gap, worth fixing on its own:
   - `GET api/admin/databaseusers` and `POST` (create) in
     [DatabaseUsersController.cs](../src/Jadwal.API/Controllers/DatabaseUsersController.cs#L26) have
     only `[Authorize]`, with no permission check.
   - `GET` also returns every connection, not just the ones the caller may use.
   - AI features must not build on those two endpoints.
6. **Audit.** Add new `AuditActions` (e.g. `AiSuggestSql`, `AiDraftDashboard`) that record who asked,
   the description, the model name and the answer. They go through the existing audit pipeline.
7. **Prompt tricks.** Table names, column comments and sample values come from the database. Someone
   could put "ignore the rules and write DELETE…" in a column comment. The SELECT-only check and
   read-only accounts make that harmless. Never let model output skip the validators.
8. **An "Allow AI" switch per connection** (Phase 1). Only some connections will get read-only
   accounts (decision 5, §11). So each connection gets a switch, off by default. Features that read
   a connection's structure or sample rows only offer that connection when the switch is on. An
   admin turns it on only for connections whose account is read-only. Phase 0 doesn't need it: it
   only uses the connection's database type, never its tables or data.

---

## 8. Arabic and the two UI languages

- The UI language already reaches the API (`Accept-Language`, `en`/`ar`). The prompt tells the model
  to **write its explanation in that language**. SQL and column names stay as they are in the
  database.
- An admin can describe the query in Arabic. Qwen3.5 and Gemma 4 both handle Arabic well, and Gemma 4
  scores highest. Business descriptions (§6) close the gap between Arabic words and English column
  names.
- New UI text goes in `en.json` and `ar.json`. New server messages go through `IAppLocalizer` /
  `Messages.resx` + `Messages.ar.resx`, not English literals.
- AI explanations shown in the Arabic UI will often mix in English SQL. Show SQL in an LTR block, as
  the SQL editor already does.

---

## 9. Offline bundle and installation

- **Keep the AI in its own bundle** (`offline-ai-bundle/`, built by a new
  `scripts/prepare-ai-bundle.ps1`), not inside today's `offline-bundle/`:
  - **Size:** today's bundle is about 1.2–1.5 GB, and a model adds 6–20 GB.
  - **Change rate:** security updates rebuild the main bundle often (see the November 2026 package
    patches), but the model rarely changes. Keeping them apart avoids re-copying gigabytes for every
    patch.
- **Contents:**
  - `ollama-windows-amd64.zip`, the standard build: CPU plus NVIDIA CUDA. The GPU's maker is not
    known yet. If it turns out to be AMD, add `ollama-windows-amd64-rocm.zip`. Use `llama-server`
    builds instead if the fallback runtime is chosen;
  - the models folder for the candidate models (manifests + blobs), pulled on an online machine with
    `ollama pull <model>`. While the GPU's memory is unknown, carry Gemma 4 12B and Qwen3.5 9B, and
    pick at install time;
  - NSSM (or a scheduled-task script) to run Ollama as a service;
  - a `MANIFEST.txt` with the versions and a **SHA-256 for every file**, so the copy to the
    air-gapped server can be checked.
- **Disk space on the build machine:** the dev machine's C: drive is nearly full (it ran out
  entirely on 2026-11-10). Build the AI bundle on another drive, or on another machine.
- **Install steps to add to [DEPLOY-AIRGAPPED.md](../DEPLOY-AIRGAPPED.md):**
  1. Unzip Ollama to, e.g., `D:\Ollama`, and copy the models folder to `D:\OllamaModels`.
  2. Set `OLLAMA_MODELS=D:\OllamaModels`, `OLLAMA_HOST=127.0.0.1:11434`, `OLLAMA_KEEP_ALIVE=-1`
     and `OLLAMA_NUM_PARALLEL=1`.
  3. Register `ollama serve` as a service set to start automatically.
  4. Check it with `ollama list` and a test prompt.
  5. In Jadwal's `appsettings.json`, set `Ai:Enabled` to `true` and `Ai:Model` to the model that was
     installed. The "AI assistant" switch in System Settings is on by default, so an admin can turn
     AI off without a restart. Grant the `ai.use` permission to the roles that should have it.

---

## 10. Suggested order of work (for when coding starts)

| Phase | What | Why this order |
|---|---|---|
| **0** | Ollama install + `Ai` config + health check + **Explain / fix SQL**. **Built 2026-11-10**, except the AI bundle script, which waits for the GPU details. | Proves the runtime, the bundle and the speed on the real server, without schema or data access |
| **1** | Schema reader + table picker + **Write SQL from a description** + SELECT-only check + CTE fix | The most useful feature; needs the safety work |
| **2** | **Draft a dashboard**. **Built 2026-11-11**, ahead of Phase 1, which it does not need. | Reuses `probe-columns`; writes no SQL |
| **3** | **Draft a report** | The most complex form; builds on 1 and 2 |
| later | Business descriptions for tables and columns; keyword or embedding ranking | Only if quality or speed asks for it |

**A test set for every phase:** about 20 real requests per database type the user cares about
(e.g. Oracle and SQL Server), with the expected result for each. Run them against each candidate
model and record:
- whether the SQL ran;
- whether the answer was right;
- how long it took on the actual server.

This, not public benchmarks, should pick the model.

---

## 11. Decisions (2026-11-10)

| # | Question | Decision | What it changes |
|---|---|---|---|
| 1 | Server hardware | **A GPU with 8–16 GB of memory.** Its maker and exact size are not known yet. | The default model is Gemma 4 12B, picked by GPU memory at install time (§4). The bundle ships Ollama's standard CPU + CUDA build (§9). |
| 2 | Licences | **Other open licences are allowed after a review**, not only Apache 2.0 / MIT. | Llama, Laguna and the Arabic-first models move from "avoid" to "after review" (§4). Codestral stays out. |
| 3 | Sample rows | **Off by default; an admin can turn them on** in System Settings. | Phase 1 adds the setting (§6). |
| 4 | Bundle | **A separate AI bundle.** | `offline-ai-bundle/` and `scripts/prepare-ai-bundle.ps1` (§9). |
| 5 | Read-only accounts | **Some connections only.** | An "Allow AI" switch on each connection (§7, item 8), in Phase 1. |

Still open: the GPU's maker (NVIDIA or AMD) and its exact memory. Find these out before building the
AI bundle.

---

## Sources

Checked 2026-11-10.

- Ollama releases and Windows version: [Releasebot – Ollama](https://releasebot.io/updates/ollama), [Softpedia – Ollama](https://www.softpedia.com/get/Artificial-Intelligence-Generative-AI/Ollama.shtml)
- Ollama model sizes and RAM: [Ollama Models List (November 2026)](https://convly.ai/ollama-models-list-2026/)
- Ollama structured output: [Ollama docs – Structured Outputs](https://docs.ollama.com/capabilities/structured-outputs), [Ollama blog](https://ollama.com/blog/structured-outputs)
- Ollama offline / as a service: [Ollama air-gapped install guide](https://markaicode.com/ollama-offline-installation-guide/), [Copy Ollama models offline](https://localaimaster.com/blog/transfer-ollama-models-offline), [Ollama on Windows as a service](https://medium.com/@niteen.gokhale/ollama-on-windows-a-neat-tidy-way-c2022571ad08)
- llama.cpp server and JSON schema: [server README](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md), [release b12229](https://github.com/ggml-org/llama.cpp/releases/tag/b12229)
- Coding / SQL models: [Best local LLMs for coding 2026](https://www.promptquorum.com/local-llms/best-local-llms-for-coding), [Best open-weight LLMs 2026](https://sysart.consulting/insights/best-open-weight-llms-2026/)
- Qwen3.5 licence: [ollama.com – qwen3.5:9b](https://ollama.com/library/qwen3.5:9b)
- Gemma 4 licence: [Google – Gemma 4](https://blog.google/innovation-and-ai/technology/developers-tools/gemma-4/)
- Arabic quality: [Arabic LLMs for enterprise 2026](https://globaluni.ai/blog/arabic-llms-enterprise-2026), [HELM Arabic](https://crfm.stanford.edu/helm/arabic/latest/), [Gemma 4 vs Qwen 3.5](https://www.mindstudio.ai/blog/gemma-4-vs-qwen-3-5-open-weight-comparison)
- CPU speed: [CPU-only LLMs 2026](https://insiderllm.com/guides/cpu-only-llms-what-actually-works/), [CPU-only inference 2026](https://enclavetools.com/blog/cpu-only-inference-2026)
- .NET clients: [Microsoft.Extensions.AI with OpenAI-compatible endpoints](https://codetraveler.io/2026/08/17/connecting-to-any-openai-compatible-endpoint-with-microsoft-extensions-ai-openai/), [MEAI going local: Ollama and Foundry Local](https://blog.senseof.tech/2026/09/12/meai-4-local-ollama-and-foundry-local/), [LLamaSharp releases](https://github.com/SciSharp/LLamaSharp/releases)
- Foundry Local: [Foundry Local is generally available](https://devblogs.microsoft.com/foundry/foundry-local-ga/)
