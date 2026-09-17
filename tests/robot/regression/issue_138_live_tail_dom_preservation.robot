*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-138    permanent    ui    speech    live-tail

*** Test Cases ***
Active Playback Retains DOM Identity During Live Tail Growth
    ${result}=    Run Process    ${APP_EXE}    --test    live-tail-dom    stdout=PIPE    stderr=STDOUT
    Should Be Equal As Integers    ${result.rc}    0
    ...    msg=AgentPanelSpeaker live-tail suite failed. Output: ${result.stdout}
    Should Contain
    ...    ${result.stdout}
    ...    PASS  live-tail-dom/production-live-playback-and-source-appends
    Should Contain
    ...    ${result.stdout}
    ...    TEST-SUITE-COMPLETE live-tail-dom exit=0
