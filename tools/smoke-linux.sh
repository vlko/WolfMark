#!/usr/bin/env bash
# Smoke test of the macOS/Linux app (Avalonia) on Linux: runs the real executable on isolated demo data, like
# tools/smoke-windows.ps1 does for the Windows app.
#
#   instance  M0-05  a second launch on the same data hands over to the first and exits; other data = own instance
#   files     M8-08  a .md file as argument: outside the notes its own window (also handed over by a second launch,
#                    never twice), inside the notes the main editor – xdg-mime is not touched
#   quicknote M8-01  --quick-note: a later launch opens the quick note window in the running app (once, however often
#                    it is asked for), a first launch with it starts the app with that window
#   links     M8-03  wolfmark:// links on the command line (what the protocol handler and the launcher actions
#                    run): a later launch hands its link to the running app, a first launch opens it
#   startup   M4-09  process start to typing, median of 3 warm starts, against --budget-ms (report only by default)
#
# Usage: tools/smoke-linux.sh <WolfMark executable> [work dir] [--budget-ms N]
#   dotnet publish src/WolfMark.Desktop -c Release -r linux-x64 -o publish/linux-x64   (or a plain build)
#   tools/smoke-linux.sh publish/linux-x64/WolfMark.Desktop
# Without $DISPLAY it starts its own Xvfb. Window titles come from tools/x11-window-titles.py (libX11, no xdotool).
# In GitHub Actions failures become annotations and a step summary. Exit code 1 when a check failed.
set -uo pipefail

exe=$(realpath "${1:?usage: smoke-linux.sh <WolfMark executable> [work dir] [--budget-ms N]}")
work=$(realpath -m "${2:-${TMPDIR:-/tmp}/wolfmark-smoke}")
budget=0
if [[ "${3:-}" == "--budget-ms" ]]; then budget=${4:?}; fi
here=$(dirname "$(realpath "$0")")
titles_py="$here/x11-window-titles.py"

rm -rf "$work" && mkdir -p "$work"
pids=()
results=()
failed=0

cleanup() {
    for p in "${pids[@]}"; do kill "$p" 2>/dev/null; done
    [[ -n "${xvfb:-}" ]] && kill "$xvfb" 2>/dev/null
}
trap cleanup EXIT

if [[ -z "${DISPLAY:-}" ]]; then
    export DISPLAY=:93
    Xvfb "$DISPLAY" -screen 0 1600x1000x24 >/dev/null 2>&1 &
    xvfb=$!
    sleep 1
fi

report() { # check ok detail
    local check=$1 ok=$2 detail=$3
    if [[ $ok == 1 ]]; then
        echo "PASS $check – $detail"
        results+=("| ✅ | $check | ${detail//$'\n'/<br>} |")
    else
        echo "FAIL $check – $detail"
        results+=("| ❌ | $check | ${detail//$'\n'/<br>} |")
        failed=1
        if [[ -n "${GITHUB_ACTIONS:-}" ]]; then
            local msg=${detail//'%'/'%25'}; msg=${msg//$'\n'/'%0A'}
            local title="Smoke test (Linux) – ${check//:/%3A}"
            echo "::error title=${title//,/%2C}::$msg"
        fi
    fi
}

log_of() { cat "$1"/local/logs/wolfmark-*.log 2>/dev/null; }
problems() { log_of "$1" | grep -E '\[(CRT|ERR)\]'; }
titles() { python3 -I "$titles_py"; }
count_title() { titles | grep -c -- "$1"; } # windows whose title contains $1

launch() { # demo dir, extra args... → pid in $launched
    (cd "$work" && exec "$exe" --demo "$@") >>"$work/stdout.txt" 2>&1 &
    launched=$!
    pids+=("$launched")
}

wait_for() { # seconds, condition (a command)
    local deadline=$((SECONDS + $1)); shift
    while ((SECONDS < deadline)); do
        "$@" && return 0
        sleep 0.2
    done
    return 1
}

wait_exit() { # pid seconds → exit code in $code, 1 when still running
    local deadline=$((SECONDS + $2))
    while kill -0 "$1" 2>/dev/null; do
        ((SECONDS >= deadline)) && return 1
        sleep 0.2
    done
    wait "$1"; code=$?
    return 0
}

stop_all() {
    for p in "${pids[@]}"; do kill "$p" 2>/dev/null; done
    for p in "${pids[@]}"; do wait "$p" 2>/dev/null; done
    pids=()
}

has_log() { log_of "$1" | grep -q -- "$2"; }

# --- instance (M0-05) ------------------------------------------------------------------------------------------
demo="$work/demo-instance"
launch "$demo"; first=$launched
if wait_for 90 has_log "$demo" "Ready to type"; then
    report "instance: first launch shows a window" 1 "pid $first, windows: $(titles | paste -sd'|')"
    launch "$demo"; second=$launched
    if wait_exit "$second" 30; then exited=1; else exited=0; code=-; fi
    sleep 1
    redirected=0; has_log "$demo" "Activation redirected from another launch (with file: False)" && redirected=1
    running=0; kill -0 "$first" 2>/dev/null && running=1
    ok=0; [[ $exited == 1 && $code == 0 && $running == 1 && $redirected == 1 ]] && ok=1
    report "instance: second launch hands over and exits" $ok "second exited: $exited (code $code), first still running: $running, redirect logged: $redirected"

    other="$work/demo-instance-other"
    launch "$other"
    ok=0; wait_for 90 has_log "$other" "Ready to type" && [[ $(count_title '^WolfMark$') -ge 2 ]] && ok=1
    report "instance: other data folder runs separately" $ok "main windows: $(count_title '^WolfMark$')"
    p=$( { problems "$demo"; problems "$other"; } )
    ok=0; [[ -z "$p" ]] && ok=1
    report "instance: no errors logged" $ok "${p:-clean logs}"
else
    report "instance: first launch shows a window" 0 "no 'Ready to type' within 90 s$(printf '\n%s' "$(tail -5 "$work/stdout.txt")")"
fi
stop_all

# --- files (M8-08) ---------------------------------------------------------------------------------------------
demo="$work/demo-files"
mkdir -p "$work/outside"
printf '# Outside one\n\nA README somewhere in a repository.\n' >"$work/outside/outside-one.md"
printf '# Outside two\n' >"$work/outside/outside-two.md"
launch "$demo" "$work/outside/outside-one.md"; main=$launched
if wait_for 90 bash -c "python3 -I '$titles_py' | grep -q '^outside-one.md '"; then
    report "files: file outside the notes opens its own window" 1 "windows: $(titles | paste -sd'|')"

    launch "$demo" "$work/outside/outside-two.md"; p2=$launched
    if wait_exit "$p2" 30; then exited=1; else exited=0; fi
    opened=0; wait_for 30 bash -c "python3 -I '$titles_py' | grep -q '^outside-two.md '" && opened=1
    logged=0; has_log "$demo" "Activation redirected from another launch (with file: True)" && logged=1
    ok=0; [[ $exited == 1 && $opened == 1 && $logged == 1 ]] && ok=1
    report "files: second launch opens its file in the running app" $ok "second exited: $exited, window in the first: $opened, redirect with file logged: $logged"

    launch "$demo" "$work/outside/outside-two.md"; p3=$launched
    if wait_exit "$p3" 30; then exited=1; else exited=0; fi
    sleep 2
    n=$(count_title '^outside-two.md ')
    ok=0; [[ $exited == 1 && $n == 1 ]] && ok=1
    report "files: the same file again reuses its window" $ok "second exited: $exited, windows for outside-two.md: $n"

    note=$(find "$demo/notes" -name '*.md' -not -path '*/.wolfmark/*' | head -1)
    name=$(basename "$note")
    launch "$demo" "$note"; p4=$launched
    if wait_exit "$p4" 30; then exited=1; else exited=0; fi
    sleep 2
    n=$(count_title "^$name ")
    running=0; kill -0 "$main" 2>/dev/null && running=1
    ok=0; [[ $exited == 1 && $n == 0 && $running == 1 ]] && ok=1
    report "files: a note of the notes tree opens in the main editor" $ok "$name: second exited: $exited, own windows: $n, app running: $running"

    p=$(problems "$demo")
    ok=0; [[ -z "$p" ]] && ok=1
    report "files: no errors logged" $ok "${p:-clean log}"
else
    report "files: file outside the notes opens its own window" 0 "no window for outside-one.md; windows: $(titles | paste -sd'|')"
fi
stop_all

# --- quick note (M8-01) ---------------------------------------------------------------------------------------
quick_title='^(Quick note|Rýchla poznámka) – WolfMark$'
quick_count() { titles | grep -cE -- "$quick_title"; }
demo="$work/demo-quicknote"
launch "$demo"; main=$launched
if wait_for 90 has_log "$demo" "Ready to type"; then
    launch "$demo" --quick-note; p2=$launched
    if wait_exit "$p2" 30; then exited=1; else exited=0; fi
    shown=0; wait_for 30 bash -c "python3 -I '$titles_py' | grep -qE '$quick_title'" && shown=1
    logged=0; wait_for 10 has_log "$demo" "Quick note shown" && logged=1
    ok=0; [[ $exited == 1 && $shown == 1 && $logged == 1 ]] && ok=1
    report "quicknote: a later launch opens it in the running app" $ok "second exited: $exited, window: $shown, logged: $logged; windows: $(titles | paste -sd'|')"

    launch "$demo" --quick-note; p3=$launched
    if wait_exit "$p3" 30; then exited=1; else exited=0; fi
    sleep 2
    n=$(quick_count)
    ok=0; [[ $exited == 1 && $n == 1 ]] && ok=1
    report "quicknote: asked again, the same window" $ok "second exited: $exited, quick note windows: $n"
    p=$(problems "$demo")
    ok=0; [[ -z "$p" ]] && ok=1
    report "quicknote: no errors logged" $ok "${p:-clean log}"
else
    report "quicknote: a later launch opens it in the running app" 0 "no 'Ready to type' within 90 s"
fi
stop_all

demo="$work/demo-quicknote-first"
launch "$demo" --quick-note
ok=0; wait_for 90 bash -c "python3 -I '$titles_py' | grep -qE '$quick_title'" && ok=1
report "quicknote: a first launch with it starts the app with the window" $ok "windows: $(titles | paste -sd'|')"
stop_all

# --- links (M8-03) --------------------------------------------------------------------------------------------
link_count() { log_of "$1" | grep -c -- "Link opened: $2"; }
demo="$work/demo-links"
launch "$demo"
if wait_for 90 has_log "$demo" "Ready to type"; then
    for pair in "wolfmark://day/2026-11-10 Day" "wolfmark://search?q=release Search" "wolfmark://today Today"; do
        link=${pair% *}; kind=${pair#* }
        launch "$demo" "$link"; p=$launched
        if wait_exit "$p" 30; then exited=1; else exited=0; fi
        opened=0; wait_for 10 bash -c "[[ \$(cat '$demo'/local/logs/wolfmark-*.log 2>/dev/null | grep -c 'Link opened: $kind') -ge 1 ]]" && opened=1
        ok=0; [[ $exited == 1 && $opened == 1 ]] && ok=1
        report "links: $link in the running app" $ok "second exited: $exited, opened: $opened"
    done
    note=$(find "$demo/notes" -name '*.md' -not -path '*/.wolfmark/*' | head -1)
    rel=${note#"$demo/notes/"}; rel=${rel%.md}
    launch "$demo" "wolfmark://note/$rel"; p=$launched
    wait_exit "$p" 30
    ok=0; wait_for 10 has_log "$demo" "Link opened: Note" && ok=1
    report "links: a note link opens the note" $ok "wolfmark://note/$rel"
    p=$(problems "$demo")
    ok=0; [[ -z "$p" ]] && ok=1
    report "links: no errors logged" $ok "${p:-clean log}"
else
    report "links: wolfmark://day/2026-11-10 in the running app" 0 "no 'Ready to type' within 90 s"
fi
stop_all

demo="$work/demo-links-first"
launch "$demo" "wolfmark://today"
ok=0; wait_for 90 has_log "$demo" "Link opened: Today" && ok=1
report "links: a first launch opens its link" $ok "$(log_of "$demo" | grep -o 'Link opened: [A-Za-z]*' | paste -sd' ')"
stop_all

# --- startup (M4-09) -------------------------------------------------------------------------------------------
demo="$work/demo-startup"
times=()
for run in 0 1 2 3; do # start 0 creates the demo notes and the index – not measured
    before=$(log_of "$demo" | grep -c "Ready to type")
    launch "$demo"
    wait_for 90 bash -c "[[ \$(cat '$demo'/local/logs/wolfmark-*.log 2>/dev/null | grep -c 'Ready to type') -gt $before ]]"
    stop_all
    ms=$(log_of "$demo" | grep -o 'Ready to type [0-9]* ms' | tail -1 | grep -o '[0-9]*')
    ((run > 0)) && [[ -n "$ms" ]] && times+=("$ms")
    sleep 0.5
done
if ((${#times[@]} == 3)); then
    median=$(printf '%s\n' "${times[@]}" | sort -n | sed -n 2p)
    ok=1; ((budget > 0 && median > budget)) && ok=0
    report "startup: ready to type" $ok "median $median ms$( ((budget > 0)) && echo ", budget $budget ms") – ${times[*]} ms"
else
    report "startup: ready to type" 0 "only ${#times[@]} of 3 starts logged 'Ready to type'"
fi

if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
    { echo "### Linux smoke test"; echo; echo "| | Check | Detail |"; echo "|---|---|---|"; printf '%s\n' "${results[@]}"; } >>"$GITHUB_STEP_SUMMARY"
fi
echo
echo "$(grep -c . <<<"$(printf '%s\n' "${results[@]}" | grep '✅')")/${#results[@]} checks passed; work folder: $work"
exit $failed
