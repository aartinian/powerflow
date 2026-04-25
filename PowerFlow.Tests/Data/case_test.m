function mpc = case_test
%CASE_TEST  Minimal 3-bus case for parser unit tests.
%   Derived from IEEE 14-bus data; values match the published case file.

mpc.version = '2';
mpc.baseMVA = 100;

%% bus data
%  bus_i  type  Pd     Qd     Gs  Bs  area  Vm      Va       baseKV  zone  Vmax  Vmin
mpc.bus = [
    1      3     0      0      0   0   1     1.060   0        132     1     1.06  0.94;
    2      2     21.7   12.7   0   0   1     1.045   -4.98    132     1     1.06  0.94;
    3      1     94.2   19.0   0   0   1     1.010   -12.72   132     1     1.06  0.94;
];

%% generator data
%  bus  Pg      Qg      Qmax  Qmin   Vg     mBase  status  Pmax    Pmin
mpc.gen = [
    1   232.4   -16.9   10    0      1.06   100    1       332.4   0;
    2   40      42.4    50    -40    1.045  100    1       140     0;
];

%% gencost data — must not be picked up when parsing mpc.gen
mpc.gencost = [
    2   0   0   3   0.0430   20   0;
    2   0   0   3   0.0250   20   0;
];

%% branch data
%  fbus  tbus  r        x        b       rateA  rateB  rateC  ratio  angle  status  angmin  angmax
mpc.branch = [
    1     2     0.01938  0.05917  0.0528  0      0      0      0      0      1       -360    360;
    1     3     0.05403  0.22304  0.0492  0      0      0      0      0      1       -360    360;
    2     3     0.04699  0.19797  0.0438  0      0      0      0      0      0       -360    360;
];
