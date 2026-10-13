const nextJest = require("next/jest");

const createJestConfig = nextJest({ dir: "./" });

/** @type {import('jest').Config} */
const customJestConfig = {
  testEnvironment: "jsdom",
  setupFilesAfterEnv: ["<rootDir>/jest.setup.ts"],
  moduleNameMapper: {
    "^@/(.*)$": "<rootDir>/src/$1",
  },
  // .next/standalone contient une copie de package.json qui entre en collision avec celui de la
  // racine du projet (Haste module naming collision). Pattern volontairement sans <rootDir> : ce
  // repo contient un "+" dans son chemin, un métacaractère regex qui casserait un ancrage sur
  // <rootDir> (il serait interprété comme un quantificateur, pas comme un "+" littéral).
  modulePathIgnorePatterns: ["\\.next/"],
};

module.exports = createJestConfig(customJestConfig);
