import type { Metadata } from "next";

import "./globals.css";

export const metadata: Metadata = {
  title: "OWL — Workstation Operations",
  description: "Authorized BPO workstation operations visibility",
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}