import { defineConfig } from "blume";

export default defineConfig({
  title: "dotnet-coupling",
  description:
    ".NET ソリューションの結合度を可視化・スコアリングする静的解析 CLI ツールのドキュメント。",

  // GitHub Pages のプロジェクトページ（https://tk42.github.io/dotnet-coupling/）で配信する。
  deployment: {
    site: "https://tk42.github.io",
    base: "/dotnet-coupling",
  },

  // Edit-this-page / ヘッダーのリポジトリリンク。
  github: {
    owner: "tk42",
    repo: "dotnet-coupling",
    branch: "main",
    dir: "website",
  },

  // 日英併記（日本語をルート、英語を /en/ 配下）。未訳ページは日本語にフォールバック。
  i18n: {
    defaultLocale: "ja",
    fallbackLocale: "ja",
    parser: "dir",
    locales: [
      { code: "ja", label: "日本語" },
      { code: "en", label: "English" },
    ],
  },

  theme: {
    accent: "violet",
  },

  navigation: {
    repo: true,
  },
});
