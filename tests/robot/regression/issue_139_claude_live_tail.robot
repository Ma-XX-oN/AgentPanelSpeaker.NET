*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-139    permanent    claude    live-tail

*** Test Cases ***
Claude Live Tail Speech Suite Passes
    ${result}=    Run Process    ${APP_EXE}    --test    claude-live-tail    stdout=PIPE    stderr=STDOUT
    Should Be Equal As Integers    ${result.rc}    0
    ...    msg=AgentPanelSpeaker Claude live-tail suite failed. Output: ${result.stdout}
    Should Contain
    ...    ${result.stdout}
    ...    PASS  claude-live-tail/appended-record-reaches-speech-history
    Should Contain
    ...    ${result.stdout}
    ...    PASS  claude-live-tail/preindexed-history-repeated-appends-reach-speech-history
    Should Contain
    ...    ${result.stdout}
    ...    TEST-SUITE-COMPLETE claude-live-tail exit=0
