function mpc = case_shunt
% Minimal 2-bus fixture for mpc.shunt block parsing.
% Bus 2 has Gs=0 Bs=0 in mpc.bus; a 10 MVAr capacitor is declared in mpc.shunt.
% The parser must fold the shunt into bus 2's admittance so the solver sees Bs=10.

mpc.version = '2';

mpc.baseMVA = 100;

mpc.bus = [
%	bus	type	Pd	Qd	Gs	Bs	area	Vm	Va	baseKV	zone	Vmax	Vmin
	1	3	0	0	0	0	1	1.0	0	100	1	1.1	0.9;
	2	1	100	50	0	0	1	1.0	0	100	1	1.1	0.9;
];

mpc.gen = [
%	bus	Pg	Qg	Qmax	Qmin	Vg	mBase	status	Pmax	Pmin
	1	200	0	300	-300	1.0	100	1	400	0;
];

mpc.branch = [
%	fbus	tbus	r	x	b	rateA	rateB	rateC	ratio	angle	status	angmin	angmax
	1	2	0.02	0.10	0	200	200	200	0	0	1	-360	360;
];

mpc.shunt = [
%	bus	Gs	Bs
	2	0	10;
];
