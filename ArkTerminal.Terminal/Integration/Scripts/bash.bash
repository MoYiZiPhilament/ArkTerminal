# This rc is supplied through an inherited descriptor, not ~/.bashrc.
if [[ __ARK_LOGIN__ == 1 ]]; then
    if [[ __ARK_PROFILE__ == 1 ]]; then
    [[ -r /etc/profile ]] && source /etc/profile
    for __ark_profile in ~/.bash_profile ~/.bash_login ~/.profile; do
        if [[ -r "$__ark_profile" ]]; then source "$__ark_profile"; break; fi
    done
    fi
elif [[ __ARK_RC__ == 1 && -r ~/.bashrc ]]; then
    source ~/.bashrc
fi
__ark_token='__ARK_TOKEN__'
__ark_emit() { printf '\033]633;Ark;%s;%s;%s\033\\' "$__ark_token" "$1" "$2"; }
__ark_b64() { printf '%s' "$1" | base64 -w0; }
__ark_last_history=''
__ark_start() {
    local entry command=''
    entry=$(HISTTIMEFORMAT= builtin history 1)
    if [[ -n "$entry" && "$entry" != "$__ark_last_history" ]]; then
        command="${entry#*[0-9]  }"
        [[ "$command" == "$entry" ]] && command=''
    fi
    __ark_emit start "$(__ark_b64 "$command")"
}
__ark_prompt() {
    local code=$? cwd
    __ark_emit end "$code"
    cwd=$(cygpath -aw -- "$PWD" 2>/dev/null) || cwd=''
    __ark_emit cwd "$(__ark_b64 "$cwd")"
    __ark_emit prompt ''
    __ark_last_history=$(HISTTIMEFORMAT= builtin history 1)
}
if (( BASH_VERSINFO[0] > 4 || (BASH_VERSINFO[0] == 4 && BASH_VERSINFO[1] >= 4) )); then
    PS0="${PS0-}"'$(__ark_start)'
    __ark_emit cap 15
else
    __ark_emit cap 9
fi
if declare -p PROMPT_COMMAND 2>/dev/null | command grep -q 'declare -a'; then
    PROMPT_COMMAND=(__ark_prompt "${PROMPT_COMMAND[@]}")
else
    PROMPT_COMMAND="__ark_prompt${PROMPT_COMMAND:+; $PROMPT_COMMAND}"
fi
