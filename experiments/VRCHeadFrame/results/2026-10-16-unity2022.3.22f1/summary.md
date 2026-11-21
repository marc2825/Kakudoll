# Head Frame GPU sweep
UTC: 2026-10-16T02:42:48.6230327Z
Unity: 2022.3.22f1
OS: Windows 11  (10.0.26200) 64bit
GPU: NVIDIA GeForce RTX 3060 Ti / Direct3D11 / Direct3D 11.0 [level 11.1]
Color space: Gamma; RT: linear ARGBFloat 64x64, MSAA off
Angle tolerance: 0.05 deg; world pixel tolerance: 0.0001 m; coverage difference <= 2 pixels.
NonUniform / NegativeScale are diagnostic INCONCLUSIVE, excluded from supported gate.
gpu_skinning is the PlayerSettings toggle, not proof of the Editor skinning execution backend.
CPU BakeMesh(false) is checked with both rigid-Transform and full-Transform world reconstruction.
Analytic angle checks: 11 PASS.

GPU skinning setting=0, alternate axes=0
- Baseline: FAIL; failures=42/56; max angle=57.4125671 deg; max world=0 m; max calibration drift=0.984245658; max CPU bake rigid error=2.73142803E-07 m
- A_HeadRoot: PASS; failures=0/56; max angle=0.00387396361 deg; max world=9.65245704E-07 m; max calibration drift=1; max CPU bake rigid error=2.73142803E-07 m
- B_ChildProxyRoot: PASS; failures=0/56; max angle=0.00387396361 deg; max world=9.65245704E-07 m; max calibration drift=1; max CPU bake rigid error=2.73142803E-07 m
- B_ConstraintProxyRoot: PASS; failures=0/56; max angle=0.00387396361 deg; max world=9.61096021E-07 m; max calibration drift=1; max CPU bake rigid error=2.73142803E-07 m
- B_RendererOnly: FAIL; failures=42/56; max angle=57.4125671 deg; max world=0 m; max calibration drift=0.984245658; max CPU bake rigid error=4.95113341E-07 m
- C_RebasedChild: PASS; failures=0/56; max angle=0.00387396361 deg; max world=8.01898466E-07 m; max calibration drift=1; max CPU bake rigid error=2.92001943E-07 m
- C_RebasedConstraint: PASS; failures=0/56; max angle=0.00387396361 deg; max world=9.62942522E-07 m; max calibration drift=1; max CPU bake rigid error=4.95113341E-07 m
Source mesh fingerprint unchanged: PASS (e39119c821ba687adfd42d2f7cd5cc2b5d2cff4361ab694640f1b321c6fc01f0)

GPU skinning setting=0, alternate axes=1
- Baseline: FAIL; failures=42/56; max angle=57.4125671 deg; max world=0 m; max calibration drift=0.984245539; max CPU bake rigid error=5.36441803E-07 m
- A_HeadRoot: PASS; failures=0/56; max angle=0.00388383865 deg; max world=1.27976602E-06 m; max calibration drift=0.999999881; max CPU bake rigid error=5.36441803E-07 m
- B_ChildProxyRoot: PASS; failures=0/56; max angle=0.00387310982 deg; max world=9.70293286E-07 m; max calibration drift=0.999999881; max CPU bake rigid error=5.36441803E-07 m
- B_ConstraintProxyRoot: PASS; failures=0/56; max angle=0.00387310982 deg; max world=9.99155873E-07 m; max calibration drift=1; max CPU bake rigid error=5.36441803E-07 m
- B_RendererOnly: FAIL; failures=42/56; max angle=57.4125671 deg; max world=0 m; max calibration drift=0.984245539; max CPU bake rigid error=4.9151248E-07 m
- C_RebasedChild: PASS; failures=0/56; max angle=0.00387310982 deg; max world=9.62942522E-07 m; max calibration drift=0.999999881; max CPU bake rigid error=4.76837158E-07 m
- C_RebasedConstraint: PASS; failures=0/56; max angle=0.00387310982 deg; max world=7.40274345E-07 m; max calibration drift=1; max CPU bake rigid error=4.76837158E-07 m
Source mesh fingerprint unchanged: PASS (643f0f828d9ac8a422c05d1d7d948333dccc5cf410740d6da39b251b612e2f4e)

GPU skinning setting=1, alternate axes=0
- Baseline: FAIL; failures=42/56; max angle=57.4125671 deg; max world=0 m; max calibration drift=0.984245658; max CPU bake rigid error=2.73142803E-07 m
- A_HeadRoot: PASS; failures=0/56; max angle=0.00387396361 deg; max world=1.20726997E-06 m; max calibration drift=1; max CPU bake rigid error=2.73142803E-07 m
- B_ChildProxyRoot: PASS; failures=0/56; max angle=0.00387396361 deg; max world=1.20726997E-06 m; max calibration drift=1; max CPU bake rigid error=2.73142803E-07 m
- B_ConstraintProxyRoot: PASS; failures=0/56; max angle=0.00387396361 deg; max world=9.62942522E-07 m; max calibration drift=1; max CPU bake rigid error=2.73142803E-07 m
- B_RendererOnly: FAIL; failures=42/56; max angle=57.4125671 deg; max world=0 m; max calibration drift=0.984245658; max CPU bake rigid error=4.95113341E-07 m
- C_RebasedChild: PASS; failures=0/56; max angle=0.00387396361 deg; max world=8.01898466E-07 m; max calibration drift=1; max CPU bake rigid error=2.92001943E-07 m
- C_RebasedConstraint: PASS; failures=0/56; max angle=0.00387396361 deg; max world=1.02026718E-06 m; max calibration drift=1; max CPU bake rigid error=4.95113341E-07 m
Source mesh fingerprint unchanged: PASS (e39119c821ba687adfd42d2f7cd5cc2b5d2cff4361ab694640f1b321c6fc01f0)

GPU skinning setting=1, alternate axes=1
- Baseline: FAIL; failures=42/56; max angle=57.4125671 deg; max world=0 m; max calibration drift=0.984245539; max CPU bake rigid error=5.36441803E-07 m
- A_HeadRoot: PASS; failures=0/56; max angle=0.00388383865 deg; max world=1.27976602E-06 m; max calibration drift=0.999999881; max CPU bake rigid error=5.36441803E-07 m
- B_ChildProxyRoot: PASS; failures=0/56; max angle=0.00387310982 deg; max world=9.84830308E-07 m; max calibration drift=0.999999881; max CPU bake rigid error=5.36441803E-07 m
- B_ConstraintProxyRoot: PASS; failures=0/56; max angle=0.00387310982 deg; max world=9.99155873E-07 m; max calibration drift=1; max CPU bake rigid error=5.36441803E-07 m
- B_RendererOnly: FAIL; failures=42/56; max angle=57.4125671 deg; max world=0 m; max calibration drift=0.984245539; max CPU bake rigid error=4.9151248E-07 m
- C_RebasedChild: PASS; failures=0/56; max angle=0.00387310982 deg; max world=9.62942522E-07 m; max calibration drift=0.999999881; max CPU bake rigid error=4.76837158E-07 m
- C_RebasedConstraint: PASS; failures=0/56; max angle=0.00387310982 deg; max world=7.34855405E-07 m; max calibration drift=1; max CPU bake rigid error=4.76837158E-07 m
Source mesh fingerprint unchanged: PASS (643f0f828d9ac8a422c05d1d7d948333dccc5cf410740d6da39b251b612e2f4e)

Recorded cases: 1960. Harness completed; candidate FAIL does not mean harness failure.
VRChat client / stereo / mirror / camera / head chop: NOT RUN.
