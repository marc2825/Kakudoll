# SDK 3.10.5 Head Frame results

Fresh staging / Unity 2022.3.22f1 / SDK base+avatars 3.10.5 / D3D11 / RTX 3060 Ti

| phase | backend | method | PASS | FAIL | max angle deg | max world error m |
| --- | --- | --- | --- | --- | --- | --- |
| EditMode | Unity | B_ChildProxyRoot | 112 | 0 | 0.003873964 | 1.20727e-06 |
| EditMode | Unity | B_ConstraintProxyRoot | 112 | 0 | 0.003873964 | 9.991559e-07 |
| EditMode | Unity | C_RebasedConstraint | 112 | 0 | 0.003873964 | 1.020267e-06 |
| EditMode | VrcDirect | B_ChildProxyRoot | 112 | 0 | 0.003873964 | 1.20727e-06 |
| EditMode | VrcDirect | B_ConstraintProxyRoot | 112 | 0 | 0.003873964 | 9.848303e-07 |
| EditMode | VrcDirect | C_RebasedConstraint | 112 | 0 | 0.003873964 | 9.555351e-07 |
| EditMode | VrcConverted | B_ChildProxyRoot | 112 | 0 | 0.003873964 | 1.20727e-06 |
| EditMode | VrcConverted | B_ConstraintProxyRoot | 112 | 0 | 0.003873964 | 9.848303e-07 |
| EditMode | VrcConverted | C_RebasedConstraint | 112 | 0 | 0.003873964 | 9.555351e-07 |
| EditMode | VrcDisabled | B_ChildProxyRoot | 112 | 0 | 0.003873964 | 1.20727e-06 |
| EditMode | VrcDisabled | B_ConstraintProxyRoot | 14 | 98 | 63.00065 | 7.152557e-07 |
| EditMode | VrcDisabled | C_RebasedConstraint | 14 | 98 | 63.00065 | 1.11987e-06 |
| PlayMode | Unity | B_ChildProxyRoot | 112 | 0 | 0.003873964 | 1.20727e-06 |
| PlayMode | Unity | B_ConstraintProxyRoot | 112 | 0 | 0.003873964 | 9.991559e-07 |
| PlayMode | Unity | C_RebasedConstraint | 112 | 0 | 0.003873964 | 1.020267e-06 |
| PlayMode | VrcDirect | B_ChildProxyRoot | 112 | 0 | 0.003873964 | 1.20727e-06 |
| PlayMode | VrcDirect | B_ConstraintProxyRoot | 112 | 0 | 0.003873964 | 9.848303e-07 |
| PlayMode | VrcDirect | C_RebasedConstraint | 112 | 0 | 0.003873964 | 9.555351e-07 |
| PlayMode | VrcConverted | B_ChildProxyRoot | 112 | 0 | 0.003873964 | 1.20727e-06 |
| PlayMode | VrcConverted | B_ConstraintProxyRoot | 112 | 0 | 0.003873964 | 9.848303e-07 |
| PlayMode | VrcConverted | C_RebasedConstraint | 112 | 0 | 0.003873964 | 9.555351e-07 |
| PlayMode | VrcDisabled | B_ChildProxyRoot | 112 | 0 | 0.003873964 | 1.20727e-06 |
| PlayMode | VrcDisabled | B_ConstraintProxyRoot | 14 | 98 | 63.00065 | 7.152557e-07 |
| PlayMode | VrcDisabled | C_RebasedConstraint | 14 | 98 | 63.00065 | 1.11987e-06 |

2688 measured cases: 2296 PASS / 392 expected negative-control FAIL.
Fresh import + SDK settings reload + Edit/Play Mode lifecycle: PASS (Unity exit code 0).
No claim of VRChat client, standalone GPU skinning backend, Stereo, Mirror, Camera or Head Chop validation.
Original S1 source/artifact SHA-256 entries: all 59 unchanged.
SDK key files (source and staging): unchanged.
