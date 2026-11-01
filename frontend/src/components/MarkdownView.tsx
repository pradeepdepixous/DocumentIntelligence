import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";

interface MarkdownViewProps {
  content: string;
  className?: string;
}

export function MarkdownView({ content, className = "" }: MarkdownViewProps) {
  if (!content) return null;

  return (
    <div className={`text-sm leading-relaxed text-gray-800 ${className}`}>
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          h1: ({ ...props }) => (
            <h1 className="mt-4 mb-2 text-base font-bold text-navy border-b border-border-subtle pb-1" {...props} />
          ),
          h2: ({ ...props }) => (
            <h2 className="mt-3.5 mb-1.5 text-sm font-bold text-navy" {...props} />
          ),
          h3: ({ ...props }) => (
            <h3 className="mt-3 mb-1 text-sm font-semibold text-navy" {...props} />
          ),
          h4: ({ ...props }) => (
            <h4 className="mt-2 mb-1 text-xs font-semibold uppercase tracking-wider text-navy/80" {...props} />
          ),
          p: ({ ...props }) => (
            <p className="mb-2.5 leading-relaxed last:mb-0" {...props} />
          ),
          ul: ({ ...props }) => (
            <ul className="my-2 ml-5 list-disc space-y-1 marker:text-navy/60" {...props} />
          ),
          ol: ({ ...props }) => (
            <ol className="my-2 ml-5 list-decimal space-y-1 marker:font-medium marker:text-navy/70" {...props} />
          ),
          li: ({ ...props }) => (
            <li className="pl-0.5 leading-relaxed" {...props} />
          ),
          strong: ({ ...props }) => (
            <strong className="font-semibold text-navy" {...props} />
          ),
          em: ({ ...props }) => (
            <em className="italic text-gray-700" {...props} />
          ),
          hr: ({ ...props }) => (
            <hr className="my-3 border-border-subtle" {...props} />
          ),
          blockquote: ({ ...props }) => (
            <blockquote className="my-2 border-l-2 border-navy/40 pl-3 italic text-gray-600 bg-white/60 py-1 rounded-r text-xs" {...props} />
          ),
          code: ({ className, children, ...props }) => {
            const isMultiline = String(children).includes("\n");
            if (!isMultiline) {
              return (
                <code className={`rounded bg-gray-200/70 px-1 py-0.5 font-mono text-xs text-navy font-medium ${className ?? ""}`} {...props}>
                  {children}
                </code>
              );
            }
            return (
              <pre className="my-2 overflow-x-auto rounded-md bg-navy-dark p-3 font-mono text-xs text-gray-100">
                <code className={className} {...props}>{children}</code>
              </pre>
            );
          },
          table: ({ ...props }) => (
            <div className="my-3 overflow-x-auto rounded-lg border border-border-subtle bg-white">
              <table className="w-full text-left text-xs" {...props} />
            </div>
          ),
          thead: ({ ...props }) => (
            <thead className="border-b border-border-subtle bg-gray-50/80 font-semibold text-navy" {...props} />
          ),
          tbody: ({ ...props }) => (
            <tbody className="divide-y divide-border-subtle" {...props} />
          ),
          th: ({ ...props }) => (
            <th className="px-3 py-2 text-xs font-semibold text-navy whitespace-nowrap" {...props} />
          ),
          td: ({ ...props }) => (
            <td className="px-3 py-2 text-xs text-gray-700" {...props} />
          ),
          a: ({ ...props }) => (
            <a className="text-blue-600 underline hover:text-blue-800" target="_blank" rel="noopener noreferrer" {...props} />
          ),
        }}
      >
        {content}
      </ReactMarkdown>
    </div>
  );
}

export default MarkdownView;
