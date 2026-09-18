*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-138    permanent    ui    live-tail

*** Test Cases ***
Live Tail DOM Preservation Suite Passes
    ${result}=    Run Process    ${APP_EXE}    --test    live-tail-dom    stdout=PIPE    stderr=STDOUT
    Should Be Equal As Integers    ${result.rc}    0
    ...    msg=AgentPanelSpeaker live-tail suite failed. Output: ${result.stdout}
    Should Contain
    ...    ${result.stdout}
    ...    PASS  live-tail-dom/live-end-refresh-retains-last-content-anchor
    Should Contain
    ...    ${result.stdout}
    ...    TEST-SUITE-COMPLETE live-tail-dom exit=0
