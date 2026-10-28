const DOTS: { cx: number; cy: number; r: number; color: string; delay: number }[] = [
  { cx: 5, cy: 35, r: 2.0, color: "#17331f", delay: 0 },
  { cx: 9, cy: 28, r: 2.3, color: "#1f4a28", delay: 150 },
  { cx: 4, cy: 20, r: 1.8, color: "#235a30", delay: 420 },
  { cx: 13, cy: 32, r: 1.9, color: "#276634", delay: 260 },
  { cx: 16, cy: 23, r: 2.5, color: "#2c7a3a", delay: 60 },
  { cx: 10, cy: 14, r: 2.1, color: "#317f3b", delay: 540 },
  { cx: 21, cy: 29, r: 2.1, color: "#3b9444", delay: 330 },
  { cx: 23, cy: 18, r: 2.6, color: "#4fab42", delay: 120 },
  { cx: 17, cy: 10, r: 2.0, color: "#5bb540", delay: 480 },
  { cx: 29, cy: 23, r: 2.3, color: "#63bb3f", delay: 210 },
  { cx: 30, cy: 12, r: 2.8, color: "#86c93d", delay: 360 },
  { cx: 24, cy: 6, r: 1.9, color: "#95cf3b", delay: 90 },
  { cx: 35, cy: 16, r: 2.4, color: "#a9d639", delay: 300 },
  { cx: 33, cy: 6, r: 2.6, color: "#bedc3b", delay: 180 },
  { cx: 37, cy: 27, r: 1.8, color: "#c3de3c", delay: 450 },
];

export default function PixousMark({
  size = 32,
  animate = false,
  className = "",
}: {
  size?: number;
  animate?: boolean;
  className?: string;
}) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 40 40"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={className}
      aria-hidden="true"
    >
      {DOTS.map((d, i) => (
        <circle
          key={i}
          cx={d.cx}
          cy={d.cy}
          r={d.r}
          fill={d.color}
          className={animate ? "animate-dot-twinkle" : undefined}
          style={animate ? { animationDelay: `${d.delay}ms` } : undefined}
        />
      ))}
    </svg>
  );
}
