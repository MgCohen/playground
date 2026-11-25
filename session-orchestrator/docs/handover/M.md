# Handover M: voice dictation

The plan for voice dictation is written, and the user did not approve it (Q8 is deferred).
A test of one local model on the CPU gave good text. No app code exists.

| Item | Value |
|---|---|
| Branch | `orch/session-4822b534`, from `main` |
| PR | None |
| Plan | `docs/plan.md`, section "M plan: voice dictation" |
| Discovery page | `docs/voice-dictation.html`, https://claude.ai/artifact/1SeKtR99MvW6c6fko6rL65 |
| Spike | `spikes/m1-dictation` (not in the solution) |
| Session role | Worker of the root orchestrator, stream M |
| State | Blocked by Q8. No process runs |

## Done and not done

| Work | State |
|---|---|
| Discovery, goals, options A to H | Done, on the page |
| Plan, steps M-1 to M-6 | Done, draft |
| Approval of the plan (Q8) | Deferred by the user |
| M-1: Parakeet TDT v3, CPU, synthetic voice | Done, numbers in `docs/plan.md` |
| M-1: the voice of the user, PC and phone | Not done |
| M-1: Portuguese | Not done |
| M-1: Whisper Large v3 Turbo | Not done |
| M-1: GPU (CUDA) | Not done |
| M-2 to M-6 | Not started |
| Live check of P1 (WebView2) and P2 (phone HTTP) | Not done |

## Rules

- Brief: never touch or interact with the live store (`session-orchestrator/store/`) or port 7777.
- Brief: for a test store, use a copy with a fake provider and the ports 7830-7839.
- Brief: write no app code before the user approves the plan, unless the user asks.
- Brief: the work of D, V, L and P, and merges, are out of scope.
- Done check: the user discussed the goals and approved the plan. Then call `mcp__orch__report` with `done`.
- The user works only in the root session (rule relayed by the root). The text of that rule arrived cut. Ask the root for the exact channel for questions.
- Instruction of 2026-11-23 about Q8: "Keep it open, apply no default, start no step. If your Parakeet spike still runs on port 7830, stop it now and record that in docs/plan.md. You can start it again after the approval."

## Answers of the user that are not in other documents

Q1 to Q7 are on the page, tab "Open questions".

- The user tests on a Samsung Fold 6. The user accepts a test of Parakeet.
- The text must come fast after stop. The reference is Claude Code: "a few seconds after 1, 2, 3".
- The phone must not send a voice note to the PC: "I prefer to do this properly".
- The session must do all installation steps that it can, and give the user only a simple test recipe.
- The user gives class recordings to a local Claude session and pays nothing. The reason is not verified. The user offered a link to that session and did not send it.

## Run recipe for the spike

Do this only after the approval of Q8.

1. Install the .NET 10 SDK (10.0.100 or a later feature band, `global.json`).
2. Get the branch: `git fetch && git switch orch/session-4822b534`.
3. Get the model (487 MB download, 670 MB on disk), in Git Bash:

   ```bash
   M="$LOCALAPPDATA/orch/models"; mkdir -p "$M"; cd "$M"
   curl -L -o parakeet-v3.tar.bz2 https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2
   tar xjf parakeet-v3.tar.bz2 && rm parakeet-v3.tar.bz2
   ```

4. From `session-orchestrator/`, run `dotnet run --project spikes/m1-dictation`. NuGet gets `org.k2fsa.sherpa.onnx` 2.0.1.
5. Open http://127.0.0.1:7830. Push Record, speak, push Stop, and read the time table.

| Variable | Default | Use |
|---|---|---|
| `M1_MODEL_DIR` | `*parakeet-tdt-0.6b-v3*` in the models folder | Model folder |
| `M1_PORT` | 7830 | Port |
| `M1_THREADS` | 8 | CPU threads |

Stop the server after the test. It listens on all network interfaces, with no sign-in.

To make the synthetic test voice in PowerShell:

```powershell
Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$f = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$s.SetOutputToWaveFile("$env:TEMP\m1-sample.wav", $f)
$s.Speak("The supervisor starts a new session in its own worktree. Then the orchestrator sends the brief and waits for the report. Please approve the default decision and archive the child session when it is done.")
$s.Dispose()
```

## Test recipe for the user

| Test | Steps |
|---|---|
| PC | The page above, in English and in Portuguese |
| Phone, Gboard | Download the offline language, use airplane mode, dictate in a field |
| Phone, FUTO Voice Input | Install the app, dictate in a field |
| Phone to Parakeet (optional) | Record in Samsung Voice Recorder. Open `http://<IP of the PC>:7830`. Push "Send a recording" |

Ask for the engine with the best text, and the seconds after stop. The phone browser cannot record on plain HTTP, so the phone uses the file upload. The phone path is not verified. If the Windows Firewall blocks port 7830, the user decides about it.

## Facts for the next session

- During the discovery, the tray (`src/Orchestrator.Tray`) and `docs/ui-drafts.html` were only on `orch/integration-final`, not on `main`. Compare the code before M-2.
- `decisions.md` has the row "Voice input | Phone dictation in the text field. No extra code" (2026-11-16). After the approval, replace it.
- The speeds in `docs/plan.md` are from an Intel Core Ultra 9 275HX with an NVIDIA RTX 5080 Laptop. On a different computer, measure again.
- "About 13 s for 5 min" is an estimate from 77.5 s of audio.
- The spike cuts audio at fixed parts of 60 s, so it can cut a word. The design in the plan cuts at pauses.
- The plan says 5 recordings but names only 4. Correct it with the user.
- To update the page, read the artifact, edit the file, and publish to the same URL.

## Next steps

1. Wait for Q8. Do not ask it. Apply no default.
2. After the approval, report done and follow the order of the plan.
3. Install the model and start the spike.
4. Get the voice tests of the user, with one in Portuguese.
5. Compare Whisper Large v3 Turbo. Try the GPU, or decode each part during the recording.
6. Record the results in `docs/plan.md` and on the page. The user picks the model.

## Open questions

**Q8 (deferred, do not ask it).**

- Question: do you approve the plan in `docs/plan.md`, section "M plan: voice dictation"?
- Context: M-1 tests Parakeet TDT v3 and Whisper Large v3 Turbo on recordings of the user, and the user picks the model. M-2 adds the transcription service to the supervisor. M-3 changes the mic in the UI to record audio. M-4 is a live test in the tray. M-5 puts the engine of the phone into the Android plan. M-6 (phrase by phrase, cloud engine) comes only on request.
- Options: approve, approve with changes, do not approve.
- Recommendation: approve. Then M-1 continues. It needs 30 s, 2 min and 5 min in English, and one recording in Portuguese.

## Only on the first computer

| Item | Loss |
|---|---|
| `%LOCALAPPDATA%\orch\models` (Parakeet) | None, download it again |
| `%TEMP%\m1-sample.wav` | None, make it again |
| Transcript of the session | Facts are in this file and the page |
| Report history in the store | Facts are in `docs/plan.md` |
