*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-138    permanent    ui    speech    live-tail

*** Test Cases ***
Active Playback Retains DOM Identity During Live Tail Growth
    [Template]    Static Method Should Return V1
    ${TEST_PROBE}
    ...    AgentPanelSpeaker.Issue138LiveTailProductionIntegrationTest
    ...    Run
    ...    []
    ...    null
