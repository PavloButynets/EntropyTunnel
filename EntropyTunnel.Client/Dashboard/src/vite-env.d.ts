/// <reference types="vite/client" />

// CSS Module type declarations
declare module '*.module.css' {
  const classes: Record<string, string>
  export default classes
}
